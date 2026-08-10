import Foundation

internal struct AnsightToolProtocolBridge {
    static let capability = "tool.exec"
    static let queryType = "tool.query"
    static let catalogType = "tool.catalog"
    static let callType = "tool.call"
    static let resultType = "tool.result"
    static let errorType = "tool.error"

    let registry: [String: RegisteredTool]
    let guardPolicy: AnsightToolGuard

    func handleIfSupported(_ json: String) throws -> String? {
        do {
            let request = try parseEnvelope(json)

            if let capability = request.capability,
               capability.caseInsensitiveCompare(Self.capability) != .orderedSame {
                return nil
            }

            let response = try handle(request)
            return try serializeEnvelope(response)
        } catch {
            guard let invalidRequest = invalidToolProtocolRequest(from: json, error: error) else {
                throw error
            }

            return try serializeEnvelope(invalidRequest)
        }
    }

    func runtimeNotInitializedResponse(for json: String) -> String {
        let request = (try? parseEnvelope(json)) ?? AnsightToolProtocolEnvelope(
            type: Self.errorType,
            id: "tool.invalid",
            payload: .object([:])
        )

        let response = createErrorEnvelope(
            request: request,
            code: "tool_runtime_not_initialized",
            message: "AnsightRuntime must be initialized before handling tool protocol messages.",
            retryable: false
        )

        return (try? serializeEnvelope(response)) ?? """
        {"type":"tool.error","id":"\(request.id).response","replyTo":"\(request.id)","capability":"\(Self.capability)","payload":{"code":"tool_runtime_not_initialized","message":"AnsightRuntime must be initialized before handling tool protocol messages.","retryable":false,"details":null}}
        """
    }

    private func handle(_ request: AnsightToolProtocolEnvelope) throws -> AnsightToolProtocolEnvelope {
        switch request.type {
        case Self.queryType:
            return createCatalogEnvelope(request: request)
        case Self.callType:
            return try executeEnvelope(request: request)
        default:
            return createErrorEnvelope(
                request: request,
                code: "tool_protocol_unknown_type",
                message: "Unsupported tool protocol message type '\(request.type)'.",
                retryable: false
            )
        }
    }

    private func createCatalogEnvelope(request: AnsightToolProtocolEnvelope) -> AnsightToolProtocolEnvelope {
        guard guardPolicy.discoveryEnabled else {
            return createErrorEnvelope(
                request: request,
                code: "tool_discovery_disabled",
                message: "Tool discovery is disabled by the current guard policy.",
                retryable: false
            )
        }

        let availabilityContext = AnsightToolAvailabilityContext(
            sessionId: request.sessionId,
            requestId: request.id
        )
        let visibleTools = registry.values
            .filter { guardPolicy.isVisible($0.descriptor) }
            .sorted {
                $0.descriptor.id.localizedCaseInsensitiveCompare($1.descriptor.id) == .orderedAscending
            }

        return AnsightToolProtocolEnvelope(
            type: Self.catalogType,
            id: "\(request.id).response",
            replyTo: request.id,
            sessionId: request.sessionId,
            payload: .object([
                "guard": .object([
                    "discoveryEnabled": .bool(guardPolicy.discoveryEnabled),
                    "executionEnabled": .bool(guardPolicy.executionEnabled),
                    "allowedScopes": .array(guardPolicy.allowedScopes.map { .string($0.rawValue) }),
                ]),
                "tools": .array(visibleTools.map {
                    Self.catalogEntry(
                        for: $0,
                        availability: $0.availability(availabilityContext)
                    )
                }),
                "count": .integer(Int64(visibleTools.count)),
            ])
        )
    }

    private func executeEnvelope(request: AnsightToolProtocolEnvelope) throws -> AnsightToolProtocolEnvelope {
        guard case .object(let payload) = request.payload else {
            return createErrorEnvelope(
                request: request,
                code: "tool_call_payload_invalid",
                message: "Tool call payload must be a JSON object.",
                retryable: false
            )
        }

        guard case .string(let toolId)? = payload["toolId"], !toolId.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return createErrorEnvelope(
                request: request,
                code: "tool_call_missing_id",
                message: "Tool call payload must include 'toolId'.",
                retryable: false
            )
        }

        guard let tool = registry[Self.normalizedToolId(toolId)] else {
            return createErrorEnvelope(
                request: request,
                code: "tool_not_found",
                message: "Tool '\(toolId)' is not registered.",
                retryable: false
            )
        }

        if let denialReason = guardPolicy.executionDenialReason(for: tool.descriptor) {
            return createErrorEnvelope(
                request: request,
                code: "tool_execution_denied",
                message: denialReason,
                retryable: false
            )
        }

        let availability = tool.availability(
            AnsightToolAvailabilityContext(sessionId: request.sessionId, requestId: request.id)
        )
        guard availability.available else {
            return createErrorEnvelope(
                request: request,
                code: availability.reasonCode ?? "tool_unavailable",
                message: availability.reason ?? "Tool '\(toolId)' is not available in the current runtime state.",
                retryable: availability.retryable,
                details: availability.jsonValue
            )
        }

        guard let execute = tool.execute else {
            return createErrorEnvelope(
                request: request,
                code: "tool_execution_failed",
                message: "Tool '\(toolId)' is registered for discovery only and cannot be executed.",
                retryable: false
            )
        }

        var flattenedArguments: [String: String] = [:]
        if case .object(let arguments)? = payload["arguments"] {
            for (key, value) in arguments {
                if let stringValue = value.stringValue {
                    flattenedArguments[key] = stringValue
                }
            }
        } else if let arguments = payload["arguments"], arguments != .null {
            return createErrorEnvelope(
                request: request,
                code: "tool_call_arguments_invalid",
                message: "Tool call 'arguments' must be a JSON object when provided.",
                retryable: false
            )
        }
        flattenedArguments[AnsightToolExecutionArgumentNames.requestId] = request.id
        if let sessionId = request.sessionId?.trimmingCharacters(in: .whitespacesAndNewlines),
           !sessionId.isEmpty {
            flattenedArguments[AnsightToolExecutionArgumentNames.sessionId] = sessionId
        }

        do {
            let result = try execute(flattenedArguments)
            if !result.success {
                return createErrorEnvelope(
                    request: request,
                    code: result.errorCode ?? "tool_execution_failed",
                    message: result.message ?? "Tool '\(toolId)' failed.",
                    retryable: false,
                    details: result.result
                )
            }

            return AnsightToolProtocolEnvelope(
                type: Self.resultType,
                id: "\(request.id).response",
                replyTo: request.id,
                sessionId: request.sessionId,
                payload: .object([
                    "toolId": .string(toolId),
                    "success": .bool(true),
                    "message": result.message.map(JSONValue.string) ?? .null,
                    "result": result.result ?? .null,
                ])
            )
        } catch {
            return createErrorEnvelope(
                request: request,
                code: "tool_execution_exception",
                message: error.localizedDescription,
                retryable: false
            )
        }
    }

    private func parseEnvelope(_ json: String) throws -> AnsightToolProtocolEnvelope {
        guard let data = json.data(using: .utf8) else {
            throw RuntimeError.invalidInput("Tool protocol envelope must be valid UTF-8.")
        }

        do {
            let envelope = try JSONDecoder().decode(AnsightToolProtocolEnvelope.self, from: data)
            guard !envelope.type.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                  !envelope.id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                throw RuntimeError.invalidInput(
                    "Tool protocol envelope must include a non-empty type and id."
                )
            }

            return envelope
        } catch {
            throw RuntimeError.invalidInput(
                "Failed to parse tool protocol envelope: \(error.localizedDescription)"
            )
        }
    }

    private func invalidToolProtocolRequest(from json: String, error: Error) -> AnsightToolProtocolEnvelope? {
        guard let data = json.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data, options: []) as? [String: Any],
              let type = object["type"] as? String,
              type == Self.queryType || type == Self.callType
        else {
            return nil
        }

        if let capability = object["capability"] as? String,
           !capability.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
           capability.caseInsensitiveCompare(Self.capability) != .orderedSame {
            return nil
        }

        let requestId = (object["id"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines)
        let sessionId = object["sessionId"] as? String
        let request = AnsightToolProtocolEnvelope(
            type: type,
            id: requestId?.isEmpty == false ? requestId! : "tool.invalid",
            sessionId: sessionId,
            payload: .object([:])
        )

        return createErrorEnvelope(
            request: request,
            code: "tool_protocol_invalid_request",
            message: "Failed to parse tool protocol envelope: \(error.localizedDescription)",
            retryable: false
        )
    }

    private func serializeEnvelope(_ envelope: AnsightToolProtocolEnvelope) throws -> String {
        let data = try JSONEncoder().encode(envelope)
        guard let json = String(data: data, encoding: .utf8) else {
            throw RuntimeError.invalidInput("Tool protocol response could not be encoded as UTF-8.")
        }

        return json
    }

    private func createErrorEnvelope(
        request: AnsightToolProtocolEnvelope,
        code: String,
        message: String,
        retryable: Bool,
        details: JSONValue? = nil
    ) -> AnsightToolProtocolEnvelope {
        AnsightToolProtocolEnvelope(
            type: Self.errorType,
            id: "\(request.id).response",
            replyTo: request.id,
            sessionId: request.sessionId,
            payload: .object([
                "code": .string(code),
                "message": .string(message),
                "retryable": .bool(retryable),
                "details": details ?? .null,
            ])
        )
    }

    static func normalizedToolId(_ toolId: String) -> String {
        toolId.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    }

    private static func catalogEntry(
        for tool: RegisteredTool,
        availability: AnsightToolAvailability
    ) -> JSONValue {
        let descriptor = tool.descriptor
        var result: [String: JSONValue] = [
            "id": .string(descriptor.id),
            "name": .string(descriptor.name),
            "description": .string(descriptor.description),
            "category": .string(descriptor.category),
            "scope": .string(descriptor.scope),
            "keywords": .string(descriptor.keywords),
            "argumentsSchema": descriptor.argumentsSchema.json,
            "resultSchema": descriptor.resultSchema.json,
            "runtime": availability.jsonValue,
            "executable": .bool(availability.available && tool.execute != nil),
        ]

        if descriptor.security.isSpecified {
            result["security"] = descriptor.security.jsonValue
        }

        return .object(result)
    }
}
