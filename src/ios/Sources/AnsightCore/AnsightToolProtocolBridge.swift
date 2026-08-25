import CryptoKit
import Foundation

internal struct AnsightToolProtocolBridge {
    static let capability = "tool.exec"
    static let queryType = "tool.query"
    static let catalogType = "tool.catalog"
    static let callType = "tool.call"
    static let batchType = "tool.batch"
    static let resultType = "tool.result"
    static let batchResultType = "tool.batch.result"
    static let errorType = "tool.error"
    static let catalogSchema = "ansight.tool-catalog.v3"

    private static let fullCatalogDetail = "full"
    private static let indexCatalogDetail = "index"
    private static let definitionsCatalogDetail = "definitions"
    private static let maximumCatalogResults = 1_000

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
        case Self.batchType:
            return try executeBatchEnvelope(request: request)
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

        let requestPayload: [String: JSONValue]
        if case .object(let payload) = request.payload {
            requestPayload = payload
        } else {
            requestPayload = [:]
        }
        let detail = Self.catalogDetail(requestPayload)
        let visibleTools = registry.values
            .filter { guardPolicy.isVisible($0.descriptor) }
            .sorted {
                $0.descriptor.id.localizedCaseInsensitiveCompare($1.descriptor.id) == .orderedAscending
            }
        let availabilityContext = AnsightToolAvailabilityContext(
            sessionId: request.sessionId,
            requestId: request.id
        )
        let toolStates = visibleTools.map { tool in
            Self.catalogToolState(
                tool: tool,
                availability: tool.availability(availabilityContext)
            )
        }
        let revision = Self.catalogRevision(states: toolStates, guardPolicy: guardPolicy)
        let availabilityRevision = Self.availabilityRevision(states: toolStates)
        let requestedRevision = Self.string(requestPayload["ifRevision"])
        let requestedAvailabilityRevision = Self.string(requestPayload["ifAvailabilityRevision"])
        let staticUnchanged = requestedRevision == revision
        let availabilityUnchanged = requestedAvailabilityRevision == nil
            || requestedAvailabilityRevision == availabilityRevision
        let isDefinitionProjection = detail == Self.definitionsCatalogDetail

        if staticUnchanged && availabilityUnchanged && !isDefinitionProjection {
            return AnsightToolProtocolEnvelope(
                type: Self.catalogType,
                id: "\(request.id).response",
                replyTo: request.id,
                sessionId: request.sessionId,
                payload: .object([
                    "schema": .string(Self.catalogSchema),
                    "revision": .string(revision),
                    "unchanged": .bool(true),
                ])
            )
        }

        let evaluatedAtUtc = ISO8601DateFormatter().string(from: Date())
        if staticUnchanged && !availabilityUnchanged && !isDefinitionProjection {
            return AnsightToolProtocolEnvelope(
                type: Self.catalogType,
                id: "\(request.id).response",
                replyTo: request.id,
                sessionId: request.sessionId,
                payload: .object([
                    "schema": .string(Self.catalogSchema),
                    "revision": .string(revision),
                    "unchanged": .bool(true),
                    "availabilityRevision": .string(availabilityRevision),
                    "evaluatedAtUtc": .string(evaluatedAtUtc),
                    "changes": Self.availabilityChanges(states: toolStates),
                ])
            )
        }

        let selectedStates = Self.applyCatalogFilters(states: toolStates, payload: requestPayload)
        let serializedTools = selectedStates.map { state in
            switch detail {
            case Self.indexCatalogDetail:
                return Self.indexCatalogEntry(state)
            case Self.definitionsCatalogDetail:
                return Self.definitionCatalogEntry(state)
            default:
                return Self.fullCatalogEntry(state)
            }
        }

        var catalogPayload: [String: JSONValue] = [
            "schema": .string(Self.catalogSchema),
            "revision": .string(revision),
            "tools": .array(serializedTools),
            "count": .integer(Int64(serializedTools.count)),
        ]
        if detail != Self.fullCatalogDetail {
            catalogPayload["detail"] = .string(detail)
        }
        if !isDefinitionProjection {
            catalogPayload["availabilityRevision"] = .string(availabilityRevision)
            catalogPayload["evaluatedAtUtc"] = .string(evaluatedAtUtc)
            catalogPayload["totalCount"] = .integer(Int64(toolStates.count))
            catalogPayload["categories"] = Self.categoryCounts(states: toolStates)
        }

        return AnsightToolProtocolEnvelope(
            type: Self.catalogType,
            id: "\(request.id).response",
            replyTo: request.id,
            sessionId: request.sessionId,
            payload: .object(catalogPayload)
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

        guard tool.execute != nil || tool.executeJSON != nil else {
            return createErrorEnvelope(
                request: request,
                code: "tool_execution_failed",
                message: "Tool '\(toolId)' is registered for discovery only and cannot be executed.",
                retryable: false
            )
        }

        var jsonArguments: [String: JSONValue] = [:]
        var flattenedArguments: [String: String] = [:]
        if case .object(let arguments)? = payload["arguments"] {
            jsonArguments = arguments
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
            let result: AnsightToolExecutionResult
            if let executeJSON = tool.executeJSON {
                let errors = AnsightToolSchemaValidator.validate(
                    schema: tool.descriptor.argumentsSchema,
                    value: .object(jsonArguments)
                )
                guard errors.isEmpty else {
                    return createErrorEnvelope(
                        request: request,
                        code: "tool_arguments_schema_invalid",
                        message: "Arguments for '\(toolId)' do not satisfy its schema.",
                        retryable: false,
                        details: AnsightToolSchemaValidator.errorsJSON(errors)
                    )
                }
                result = try executeJSON(jsonArguments)
            } else if let execute = tool.execute {
                result = try execute(flattenedArguments)
            } else {
                return createErrorEnvelope(
                    request: request,
                    code: "tool_execution_failed",
                    message: "Tool '\(toolId)' cannot be executed.",
                    retryable: false
                )
            }
            if !result.success {
                return createErrorEnvelope(
                    request: request,
                    code: result.errorCode ?? "tool_execution_failed",
                    message: result.message ?? "Tool '\(toolId)' failed.",
                    retryable: false,
                    details: result.result
                )
            }

            if tool.executeJSON != nil {
                let errors = AnsightToolSchemaValidator.validate(
                    schema: tool.descriptor.resultSchema,
                    value: result.result ?? .null
                )
                guard errors.isEmpty else {
                    return createErrorEnvelope(
                        request: request,
                        code: "tool_result_schema_invalid",
                        message: "Result from '\(toolId)' does not satisfy its schema.",
                        retryable: false,
                        details: AnsightToolSchemaValidator.errorsJSON(errors)
                    )
                }
            }

            let evidence = try capturePostCallEvidence(
                request: request,
                payload: payload,
                invokedToolId: toolId
            )

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
                    "evidence": evidence ?? .null,
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

    private func executeBatchEnvelope(request: AnsightToolProtocolEnvelope) throws -> AnsightToolProtocolEnvelope {
        guard case .object(let payload) = request.payload,
              case .array(let calls)? = payload["calls"] else {
            return createErrorEnvelope(
                request: request,
                code: "tool_batch_payload_invalid",
                message: "Tool batch payload must contain a 'calls' array.",
                retryable: false
            )
        }
        guard (1...32).contains(calls.count) else {
            return createErrorEnvelope(
                request: request,
                code: "tool_batch_size_invalid",
                message: "Tool batches must contain between 1 and 32 calls.",
                retryable: false
            )
        }

        let continueOnError = payload["continueOnError"] == .bool(true)
        var results: [JSONValue] = []
        var completed = 0
        for (index, callValue) in calls.enumerated() {
            guard case .object(let call) = callValue else {
                results.append(.object([
                    "index": .integer(Int64(index)),
                    "success": .bool(false),
                    "error": .object([
                        "code": .string("tool_batch_call_invalid"),
                        "message": .string("Each batch call must be a JSON object."),
                        "retryable": .bool(false),
                    ]),
                ]))
                if !continueOnError { break }
                continue
            }

            let callRequest = AnsightToolProtocolEnvelope(
                type: Self.callType,
                id: request.id,
                replyTo: request.replyTo,
                sessionId: request.sessionId,
                sentAt: request.sentAt,
                capability: request.capability,
                payload: .object(call)
            )
            let response = try executeEnvelope(request: callRequest)
            var item: [String: JSONValue]
            if response.type == Self.resultType, case .object(let resultPayload) = response.payload {
                item = resultPayload
            } else {
                item = [
                    "toolId": call["toolId"] ?? .null,
                    "success": .bool(false),
                    "error": response.payload,
                ]
            }
            item["index"] = .integer(Int64(index))
            item["callId"] = call["callId"] ?? .null
            results.append(.object(item))
            completed += 1
            if response.type != Self.resultType && !continueOnError { break }
        }

        let allSucceeded = results.allSatisfy {
            guard case .object(let item) = $0 else { return false }
            return item["success"] == .bool(true)
        }
        return AnsightToolProtocolEnvelope(
            type: Self.batchResultType,
            id: "\(request.id).response",
            replyTo: request.id,
            sessionId: request.sessionId,
            payload: .object([
                "success": .bool(allSucceeded),
                "completed": .integer(Int64(completed)),
                "requested": .integer(Int64(calls.count)),
                "stoppedEarly": .bool(results.count < calls.count),
                "results": .array(results),
            ])
        )
    }

    private func capturePostCallEvidence(
        request: AnsightToolProtocolEnvelope,
        payload: [String: JSONValue],
        invokedToolId: String
    ) throws -> JSONValue? {
        guard case .object(let after)? = payload["after"] else { return nil }
        let delayMilliseconds: Int64
        if case .integer(let value)? = after["delayMilliseconds"] {
            delayMilliseconds = min(max(value, 0), 2_000)
        } else {
            delayMilliseconds = 0
        }
        if delayMilliseconds > 0 {
            Thread.sleep(forTimeInterval: Double(delayMilliseconds) / 1_000)
        }

        let include: [JSONValue]
        if case .array(let requested)? = after["include"] {
            include = requested
        } else {
            include = [.string("visualTree")]
        }
        var evidence: [String: JSONValue] = [:]
        for value in include {
            guard case .string(let name) = value else { continue }
            let evidenceToolId: String?
            switch name {
            case "tree", "visualTree", "visual_tree": evidenceToolId = "ui.get_visual_tree"
            case "screenshot": evidenceToolId = "ui.get_screenshot"
            default: evidenceToolId = nil
            }
            guard let evidenceToolId, evidenceToolId != invokedToolId else { continue }
            let argumentsName = evidenceToolId == "ui.get_screenshot"
                ? "screenshotArguments"
                : "visualTreeArguments"
            let evidenceRequest = AnsightToolProtocolEnvelope(
                type: Self.callType,
                id: request.id,
                replyTo: request.replyTo,
                sessionId: request.sessionId,
                sentAt: request.sentAt,
                capability: request.capability,
                payload: .object([
                    "toolId": .string(evidenceToolId),
                    "arguments": after[argumentsName] ?? .object([:]),
                ])
            )
            evidence[name] = try executeEnvelope(request: evidenceRequest).payload
        }
        return evidence.isEmpty ? nil : .object(evidence)
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
              type == Self.queryType || type == Self.callType || type == Self.batchType
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
        let encodedEnvelope = AnsightToolProtocolEnvelope(
            type: envelope.type,
            id: envelope.id,
            replyTo: envelope.replyTo,
            sessionId: envelope.sessionId,
            sentAt: envelope.sentAt,
            capability: envelope.capability,
            payload: AnsightToolProtocolPayloadEncoding.encodeIfBeneficial(envelope.payload)
        )
        let data = try JSONEncoder().encode(encodedEnvelope)
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

    private struct CatalogToolState {
        let tool: RegisteredTool
        let availability: AnsightToolAvailability
        let executable: Bool
        let definitionRevision: String
    }

    private static func catalogToolState(
        tool: RegisteredTool,
        availability: AnsightToolAvailability
    ) -> CatalogToolState {
        CatalogToolState(
            tool: tool,
            availability: availability,
            executable: availability.available && (tool.execute != nil || tool.executeJSON != nil),
            definitionRevision: revision(of: staticDefinitionEntry(for: tool))
        )
    }

    private static func indexCatalogEntry(_ state: CatalogToolState) -> JSONValue {
        let descriptor = state.tool.descriptor
        var result: [String: JSONValue] = [
            "id": .string(descriptor.id),
            "name": .string(descriptor.name),
            "description": .string(descriptor.description),
            "category": .string(descriptor.category),
            "policy": .string(descriptor.policy.rawValue),
            "definitionRevision": .string(state.definitionRevision),
        ]
        addOptionalDiscoveryMetadata(&result, descriptor: descriptor)
        addAvailability(&result, state: state)
        return .object(result)
    }

    private static func definitionCatalogEntry(_ state: CatalogToolState) -> JSONValue {
        guard case .object(var result) = staticDefinitionEntry(for: state.tool) else {
            return .object([:])
        }
        result["definitionRevision"] = .string(state.definitionRevision)
        return .object(result)
    }

    private static func fullCatalogEntry(_ state: CatalogToolState) -> JSONValue {
        guard case .object(var result) = definitionCatalogEntry(state) else {
            return .object([:])
        }
        addAvailability(&result, state: state)
        return .object(result)
    }

    private static func staticDefinitionEntry(for tool: RegisteredTool) -> JSONValue {
        let descriptor = tool.descriptor
        var result: [String: JSONValue] = [
            "id": .string(descriptor.id),
            "name": .string(descriptor.name),
            "description": .string(descriptor.description),
            "category": .string(descriptor.category),
            "policy": .string(descriptor.policy.rawValue),
            "argumentsSchema": compactSchema(descriptor.argumentsSchema.json),
            "resultSchema": compactSchema(descriptor.resultSchema.json),
        ]
        addOptionalDiscoveryMetadata(&result, descriptor: descriptor)
        if tool.executeJSON == nil {
            result["argumentEncoding"] = .string("flattened-string")
        }
        return .object(result)
    }

    private static func addOptionalDiscoveryMetadata(
        _ result: inout [String: JSONValue],
        descriptor: AnsightToolDescriptor
    ) {
        let keywords = descriptor.keywords.trimmingCharacters(in: .whitespacesAndNewlines)
        if !keywords.isEmpty {
            result["keywords"] = .string(keywords)
        }
        let prerequisiteToolIds = Array(Set(descriptor.prerequisiteToolIds
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }))
            .sorted()
        if !prerequisiteToolIds.isEmpty {
            result["prerequisiteToolIds"] = .array(prerequisiteToolIds.map(JSONValue.string))
        }
    }

    private static func addAvailability(
        _ result: inout [String: JSONValue],
        state: CatalogToolState
    ) {
        if !state.availability.available {
            result["runtime"] = state.availability.jsonValue
        }
        if !state.executable {
            result["executable"] = .bool(false)
        }
    }

    private static func catalogRevision(
        states: [CatalogToolState],
        guardPolicy: AnsightToolGuard
    ) -> String {
        revision(of: .object([
            "schema": .string(catalogSchema),
            "guard": guardJSON(guardPolicy),
            "tools": .array(states.map { state in
                .object([
                    "id": .string(state.tool.descriptor.id),
                    "definitionRevision": .string(state.definitionRevision),
                ])
            }),
        ]))
    }

    private static func availabilityRevision(states: [CatalogToolState]) -> String {
        revision(of: .object([
            "tools": .array(states.map { state in
                .object([
                    "id": .string(state.tool.descriptor.id),
                    "runtime": availabilityState(state),
                ])
            }),
        ]))
    }

    private static func availabilityState(_ state: CatalogToolState) -> JSONValue {
        guard case .object(var result) = state.availability.jsonValue else {
            return .object([:])
        }
        if !state.executable {
            result["executable"] = .bool(false)
        }
        return .object(result)
    }

    private static func availabilityChanges(states: [CatalogToolState]) -> JSONValue {
        var changes: [String: JSONValue] = [:]
        states.filter { !$0.availability.available || !$0.executable }.forEach { state in
            changes[state.tool.descriptor.id] = availabilityState(state)
        }
        return .object(changes)
    }

    private static func categoryCounts(states: [CatalogToolState]) -> JSONValue {
        var categories: [String: JSONValue] = [:]
        Dictionary(grouping: states, by: { $0.tool.descriptor.category })
            .forEach { category, categoryStates in
                categories[category] = .integer(Int64(categoryStates.count))
            }
        return .object(categories)
    }

    private static func applyCatalogFilters(
        states: [CatalogToolState],
        payload: [String: JSONValue]
    ) -> [CatalogToolState] {
        let requestedIds = Set(stringArray(payload["ids"]))
        let queryTerms = terms(string(payload["query"]))
        let featureTerms = terms(string(payload["feature"]))
        let requestedPolicy = string(payload["policy"])
        let executableOnly = bool(payload["executableOnly"]) ?? false
        let requestedLimit = integer(payload["limit"]) ?? integer(payload["maxResults"])
            ?? maximumCatalogResults
        let limit = min(max(requestedLimit, 1), maximumCatalogResults)

        return Array(states
            .filter { requestedIds.isEmpty || requestedIds.contains($0.tool.descriptor.id) }
            .filter { requestedPolicy == nil || $0.tool.descriptor.policy.rawValue.caseInsensitiveCompare(requestedPolicy!) == .orderedSame }
            .filter { !executableOnly || $0.executable }
            .filter { matchesTerms($0.tool.descriptor, queryTerms: queryTerms, featureTerms: featureTerms) }
            .prefix(limit))
    }

    private static func matchesTerms(
        _ descriptor: AnsightToolDescriptor,
        queryTerms: [String],
        featureTerms: [String]
    ) -> Bool {
        let searchableText = normalizeSearchText([
            descriptor.id,
            descriptor.name,
            descriptor.description,
            descriptor.category,
            descriptor.keywords,
            descriptor.prerequisiteToolIds.joined(separator: " "),
        ].joined(separator: " "))
        return queryTerms.allSatisfy(searchableText.contains)
            && featureTerms.allSatisfy(searchableText.contains)
    }

    private static func catalogDetail(_ payload: [String: JSONValue]) -> String {
        switch string(payload["detail"])?.lowercased() {
        case indexCatalogDetail:
            return indexCatalogDetail
        case definitionsCatalogDetail:
            return definitionsCatalogDetail
        default:
            return fullCatalogDetail
        }
    }

    private static func string(_ value: JSONValue?) -> String? {
        guard case .string(let text)? = value else { return nil }
        let normalized = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return normalized.isEmpty ? nil : normalized
    }

    private static func stringArray(_ value: JSONValue?) -> [String] {
        guard case .array(let values)? = value else { return [] }
        return values.compactMap(string)
    }

    private static func bool(_ value: JSONValue?) -> Bool? {
        guard case .bool(let result)? = value else { return nil }
        return result
    }

    private static func integer(_ value: JSONValue?) -> Int? {
        guard case .integer(let result)? = value,
              let integer = Int(exactly: result) else { return nil }
        return integer
    }

    private static func terms(_ value: String?) -> [String] {
        Array(Set(normalizeSearchText(value ?? "")
            .split(separator: " ")
            .map(String.init)))
            .sorted()
    }

    private static func normalizeSearchText(_ value: String) -> String {
        String(value.lowercased().map { character in
            character.isLetter || character.isNumber ? character : " "
        })
    }

    private static func compactSchema(_ value: JSONValue) -> JSONValue {
        guard case .object(var object) = value else { return value }
        if object["additionalProperties"] == .bool(false) {
            object.removeValue(forKey: "additionalProperties")
        }
        if case .object(let properties)? = object["properties"] {
            object["properties"] = .object(properties.mapValues(compactSchema))
        }
        if let items = object["items"] {
            object["items"] = compactSchema(items)
        }
        return .object(object)
    }

    private static func revision(of value: JSONValue) -> String {
        let data = (try? value.jsonData()) ?? Data()
        let digest = SHA256.hash(data: data)
        return "sha256:" + digest.map { String(format: "%02x", $0) }.joined()
    }

    private static func guardJSON(_ guardPolicy: AnsightToolGuard) -> JSONValue {
        .object([
            "discoveryEnabled": .bool(guardPolicy.discoveryEnabled),
            "executionEnabled": .bool(guardPolicy.executionEnabled),
            "maxPolicy": .string(guardPolicy.maxPolicy.rawValue),
        ])
    }
}
