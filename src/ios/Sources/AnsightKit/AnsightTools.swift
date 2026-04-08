import Foundation

public enum AnsightToolScope: String, Sendable, Codable, CaseIterable {
    case read = "Read"
    case write = "Write"
    case delete = "Delete"
}

public struct AnsightToolSchema: Sendable, Codable, Equatable {
    public static let emptyObject = AnsightToolSchema(json: .object([:]))

    public let json: JSONValue

    public init(json: JSONValue = .object([:])) {
        self.json = json
    }
}

public struct AnsightToolExecutionResult: Sendable, Codable, Equatable {
    public let success: Bool
    public let message: String?
    public let errorCode: String?
    public let result: JSONValue?

    public init(success: Bool, message: String? = nil, errorCode: String? = nil, result: JSONValue? = nil) {
        self.success = success
        self.message = message
        self.errorCode = errorCode
        self.result = result
    }

    public static func success(_ result: JSONValue? = nil, message: String? = nil) -> AnsightToolExecutionResult {
        AnsightToolExecutionResult(success: true, message: message, result: result)
    }

    public static func failure(
        _ message: String,
        errorCode: String? = nil,
        result: JSONValue? = nil
    ) -> AnsightToolExecutionResult {
        AnsightToolExecutionResult(success: false, message: message, errorCode: errorCode, result: result)
    }
}

public protocol AnsightTool: Sendable {
    var descriptor: AnsightToolDescriptor { get }
    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult
}

public struct AnsightToolGuard: Sendable, Codable, Equatable {
    public static let disabled = AnsightToolGuard(
        discoveryEnabled: false,
        executionEnabled: false,
        allowedScopes: []
    )

    public static let readOnly = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: [.read]
    )

    public static let fullAccess = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: AnsightToolScope.allCases
    )

    public let discoveryEnabled: Bool
    public let executionEnabled: Bool
    public let allowedScopes: [AnsightToolScope]

    public init(
        discoveryEnabled: Bool,
        executionEnabled: Bool,
        allowedScopes: [AnsightToolScope]
    ) {
        self.discoveryEnabled = discoveryEnabled
        self.executionEnabled = executionEnabled
        self.allowedScopes = allowedScopes
    }

    public func validate() throws {
        if executionEnabled && allowedScopes.isEmpty {
            throw RuntimeError.invalidInput(
                "Tool execution cannot be enabled without at least one allowed scope."
            )
        }
    }

    fileprivate func isVisible(_ descriptor: AnsightToolDescriptor) -> Bool {
        discoveryEnabled && isAllowed(descriptor)
    }

    fileprivate func executionDenialReason(for descriptor: AnsightToolDescriptor) -> String? {
        guard executionEnabled else {
            return "Tool execution is disabled by the current guard policy."
        }

        guard isAllowed(descriptor) else {
            return "Tool scope '\(descriptor.scope)' is not enabled by the current guard policy."
        }

        return nil
    }

    private func isAllowed(_ descriptor: AnsightToolDescriptor) -> Bool {
        guard let scope = descriptor.scopeValue else {
            return false
        }

        return allowedScopes.contains(scope)
    }
}

public struct AnsightToolProtocolEnvelope: Sendable, Codable, Equatable {
    private enum CodingKeys: String, CodingKey {
        case type
        case id
        case replyTo
        case sessionId
        case sentAt
        case capability
        case payload
    }

    public let type: String
    public let id: String
    public let replyTo: String?
    public let sessionId: String?
    public let sentAt: String
    public let capability: String?
    public let payload: JSONValue

    public init(
        type: String,
        id: String,
        replyTo: String? = nil,
        sessionId: String? = nil,
        sentAt: String? = nil,
        capability: String? = nil,
        payload: JSONValue
    ) {
        self.type = type
        self.id = id
        self.replyTo = replyTo
        self.sessionId = sessionId
        self.sentAt = sentAt ?? Self.makeTimestamp()
        self.capability = capability ?? "tool.exec"
        self.payload = payload
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        type = try container.decode(String.self, forKey: .type)
        id = try container.decode(String.self, forKey: .id)
        replyTo = try container.decodeIfPresent(String.self, forKey: .replyTo)
        sessionId = try container.decodeIfPresent(String.self, forKey: .sessionId)
        sentAt = try container.decodeIfPresent(String.self, forKey: .sentAt) ?? Self.makeTimestamp()
        capability = try container.decodeIfPresent(String.self, forKey: .capability) ?? "tool.exec"
        payload = try container.decodeIfPresent(JSONValue.self, forKey: .payload) ?? .object([:])
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(type, forKey: .type)
        try container.encode(id, forKey: .id)
        try container.encodeIfPresent(replyTo, forKey: .replyTo)
        try container.encodeIfPresent(sessionId, forKey: .sessionId)
        try container.encode(sentAt, forKey: .sentAt)
        try container.encodeIfPresent(capability, forKey: .capability)
        try container.encode(payload, forKey: .payload)
    }

    public static func makeTimestamp() -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: Date())
    }
}

internal struct RegisteredTool {
    let descriptor: AnsightToolDescriptor
    let execute: (([String: String]) throws -> AnsightToolExecutionResult)?
}

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
        let request = try parseEnvelope(json)

        if let capability = request.capability,
           capability.caseInsensitiveCompare(Self.capability) != .orderedSame {
            return nil
        }

        let response = try handle(request)
        return try serializeEnvelope(response)
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

        let visibleTools = registry.values
            .map(\.descriptor)
            .filter(guardPolicy.isVisible)
            .sorted { $0.id.localizedCaseInsensitiveCompare($1.id) == .orderedAscending }

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
                "tools": .array(visibleTools.map { descriptor in
                    .object([
                        "id": .string(descriptor.id),
                        "name": .string(descriptor.name),
                        "description": .string(descriptor.description),
                        "category": .string(descriptor.category),
                        "scope": .string(descriptor.scope),
                        "keywords": .string(descriptor.keywords),
                        "argumentsSchema": descriptor.argumentsSchema.json,
                        "resultSchema": descriptor.resultSchema.json,
                    ])
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

        guard let tool = registry[toolId] else {
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
}
