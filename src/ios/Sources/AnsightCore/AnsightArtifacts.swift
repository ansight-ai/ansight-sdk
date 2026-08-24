import Foundation

public enum AnsightArtifactToolIds {
    public static let query = "artifacts.query"
    public static let request = "artifacts.request"
}

public struct AnsightArtifactProviderDescriptor: Sendable, Equatable {
    public var id: String
    public var name: String
    public var description: String?
    public var category: String
    public var tags: [String]
    public var metadata: [String: String]

    public init(
        id: String,
        name: String,
        description: String? = nil,
        category: String = "app",
        tags: [String] = [],
        metadata: [String: String] = [:]
    ) {
        self.id = id
        self.name = name
        self.description = description
        self.category = category
        self.tags = tags
        self.metadata = metadata
    }

    func validated() throws -> AnsightArtifactProviderDescriptor {
        let normalizedId = id.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedName = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedId.isEmpty else {
            throw RuntimeError.invalidInput("Artifact provider id must not be blank.")
        }
        guard !normalizedName.isEmpty else {
            throw RuntimeError.invalidInput("Artifact provider name must not be blank.")
        }

        return AnsightArtifactProviderDescriptor(
            id: normalizedId,
            name: normalizedName,
            description: description?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank,
            category: category.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank ?? "app",
            tags: Self.normalizedTags(tags),
            metadata: Self.normalizedMetadata(metadata)
        )
    }

    fileprivate func jsonValue(error: String? = nil) -> JSONValue {
        .object([
            "id": .string(id),
            "name": .string(name),
            "description": description.map(JSONValue.string) ?? .null,
            "category": .string(category),
            "tags": .array(tags.map(JSONValue.string)),
            "metadata": .object(metadata.mapValues(JSONValue.string)),
            "error": error.map(JSONValue.string) ?? .null,
        ])
    }

    fileprivate static func normalizedTags(_ tags: [String]) -> [String] {
        var seen: Set<String> = []
        return tags.compactMap { rawValue in
            let value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !value.isEmpty else {
                return nil
            }

            let key = value.lowercased()
            guard !seen.contains(key) else {
                return nil
            }

            seen.insert(key)
            return value
        }
    }

    fileprivate static func normalizedMetadata(_ metadata: [String: String]) -> [String: String] {
        var normalized: [String: String] = [:]
        for (rawKey, rawValue) in metadata {
            let key = rawKey.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !key.isEmpty else {
                continue
            }

            normalized[key] = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        }

        return normalized
    }
}

public struct AnsightArtifactContentDescriptor: Sendable, Equatable {
    public var supportedMimeTypes: [String]
    public var defaultMimeType: String?
    public var suggestedFileName: String?
    public var supportsText: Bool
    public var supportsBinary: Bool
    public var sizeKnownBeforeCreation: Bool
    public var estimatedSizeBytes: Int64?

    public init(
        supportedMimeTypes: [String],
        defaultMimeType: String? = nil,
        suggestedFileName: String? = nil,
        supportsText: Bool = false,
        supportsBinary: Bool = true,
        sizeKnownBeforeCreation: Bool = false,
        estimatedSizeBytes: Int64? = nil
    ) {
        self.supportedMimeTypes = supportedMimeTypes
        self.defaultMimeType = defaultMimeType
        self.suggestedFileName = suggestedFileName
        self.supportsText = supportsText
        self.supportsBinary = supportsBinary
        self.sizeKnownBeforeCreation = sizeKnownBeforeCreation
        self.estimatedSizeBytes = estimatedSizeBytes
    }

    fileprivate func validated() throws -> AnsightArtifactContentDescriptor {
        let mimeTypes = supportedMimeTypes.compactMap { value in
            value.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank
        }
        guard !mimeTypes.isEmpty else {
            throw RuntimeError.invalidInput("Artifact content must include at least one supported MIME type.")
        }

        let normalizedDefault = defaultMimeType?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank ?? mimeTypes[0]
        return AnsightArtifactContentDescriptor(
            supportedMimeTypes: mimeTypes,
            defaultMimeType: normalizedDefault,
            suggestedFileName: suggestedFileName?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank,
            supportsText: supportsText,
            supportsBinary: supportsBinary,
            sizeKnownBeforeCreation: sizeKnownBeforeCreation,
            estimatedSizeBytes: estimatedSizeBytes
        )
    }

    fileprivate var jsonValue: JSONValue {
        .object([
            "supportedMimeTypes": .array(supportedMimeTypes.map(JSONValue.string)),
            "defaultMimeType": defaultMimeType.map(JSONValue.string) ?? .null,
            "suggestedFileName": suggestedFileName.map(JSONValue.string) ?? .null,
            "supportsText": .bool(supportsText),
            "supportsBinary": .bool(supportsBinary),
            "sizeKnownBeforeCreation": .bool(sizeKnownBeforeCreation),
            "estimatedSizeBytes": estimatedSizeBytes.map(JSONValue.integer) ?? .null,
        ])
    }
}

public struct AnsightArtifactDefinition: Sendable, Equatable {
    public var id: String
    public var name: String
    public var description: String
    public var kind: String
    public var category: String
    public var content: AnsightArtifactContentDescriptor
    public var argumentsSchema: AnsightToolSchema
    public var policy: AnsightToolPolicy
    public var tags: [String]
    public var metadata: [String: String]

    public init(
        id: String,
        name: String,
        description: String,
        kind: String,
        category: String,
        content: AnsightArtifactContentDescriptor,
        argumentsSchema: AnsightToolSchema = .emptyObject,
        policy: AnsightToolPolicy = .read,
        tags: [String] = [],
        metadata: [String: String] = [:]
    ) {
        self.id = id
        self.name = name
        self.description = description
        self.kind = kind
        self.category = category
        self.content = content
        self.argumentsSchema = argumentsSchema
        self.policy = policy
        self.tags = tags
        self.metadata = metadata
    }

    fileprivate func validated() throws -> AnsightArtifactDefinition {
        let normalizedId = id.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedName = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedKind = kind.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedCategory = category.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedId.isEmpty else {
            throw RuntimeError.invalidInput("Artifact id must not be blank.")
        }
        guard !normalizedName.isEmpty else {
            throw RuntimeError.invalidInput("Artifact name must not be blank.")
        }
        guard !normalizedKind.isEmpty else {
            throw RuntimeError.invalidInput("Artifact kind must not be blank.")
        }
        guard !normalizedCategory.isEmpty else {
            throw RuntimeError.invalidInput("Artifact category must not be blank.")
        }

        return AnsightArtifactDefinition(
            id: normalizedId,
            name: normalizedName,
            description: description.trimmingCharacters(in: .whitespacesAndNewlines),
            kind: normalizedKind,
            category: normalizedCategory,
            content: try content.validated(),
            argumentsSchema: argumentsSchema,
            policy: policy,
            tags: AnsightArtifactProviderDescriptor.normalizedTags(tags),
            metadata: AnsightArtifactProviderDescriptor.normalizedMetadata(metadata)
        )
    }

    fileprivate func jsonValue(providerId: String) -> JSONValue {
        .object([
            "providerId": .string(providerId),
            "id": .string(id),
            "name": .string(name),
            "description": .string(description),
            "kind": .string(kind),
            "category": .string(category),
            "tags": .array(tags.map(JSONValue.string)),
            "metadata": .object(metadata.mapValues(JSONValue.string)),
            "content": content.jsonValue,
            "argumentsSchema": argumentsSchema.json,
            "policy": .string(policy.rawValue),
        ])
    }
}

public struct AnsightArtifactMetadata: Sendable, Equatable {
    public var artifactId: String
    public var providerId: String
    public var name: String
    public var kind: String
    public var mimeType: String
    public var fileName: String
    public var description: String?
    public var sizeBytes: Int64?
    public var createdAtUtc: String
    public var tags: [String]
    public var metadata: [String: String]

    public init(
        artifactId: String,
        providerId: String,
        name: String,
        kind: String,
        mimeType: String,
        fileName: String,
        description: String? = nil,
        sizeBytes: Int64? = nil,
        createdAtUtc: String = AnsightClock.isoNow(),
        tags: [String] = [],
        metadata: [String: String] = [:]
    ) {
        self.artifactId = artifactId
        self.providerId = providerId
        self.name = name
        self.kind = kind
        self.mimeType = mimeType
        self.fileName = fileName
        self.description = description
        self.sizeBytes = sizeBytes
        self.createdAtUtc = createdAtUtc
        self.tags = tags
        self.metadata = metadata
    }

    fileprivate func validated(expectedProviderId: String, expectedArtifactId: String) throws -> AnsightArtifactMetadata {
        let normalizedProviderId = providerId.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedArtifactId = artifactId.trimmingCharacters(in: .whitespacesAndNewlines)
        guard normalizedProviderId.caseInsensitiveCompare(expectedProviderId) == .orderedSame else {
            throw RuntimeError.invalidInput("Artifact metadata provider id must match the requested provider id.")
        }
        guard normalizedArtifactId.caseInsensitiveCompare(expectedArtifactId) == .orderedSame else {
            throw RuntimeError.invalidInput("Artifact metadata artifact id must match the requested artifact id.")
        }

        let normalizedName = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedKind = kind.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedMimeType = mimeType.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedFileName = fileName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedName.isEmpty else {
            throw RuntimeError.invalidInput("Artifact metadata name must be non-empty.")
        }
        guard !normalizedKind.isEmpty else {
            throw RuntimeError.invalidInput("Artifact metadata kind must be non-empty.")
        }
        guard !normalizedMimeType.isEmpty else {
            throw RuntimeError.invalidInput("Artifact metadata MIME type must be non-empty.")
        }
        guard !normalizedFileName.isEmpty else {
            throw RuntimeError.invalidInput("Artifact metadata file name must be non-empty.")
        }

        return AnsightArtifactMetadata(
            artifactId: normalizedArtifactId,
            providerId: normalizedProviderId,
            name: normalizedName,
            kind: normalizedKind,
            mimeType: normalizedMimeType,
            fileName: normalizedFileName,
            description: description?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank,
            sizeBytes: sizeBytes,
            createdAtUtc: createdAtUtc.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank ?? AnsightClock.isoNow(),
            tags: AnsightArtifactProviderDescriptor.normalizedTags(tags),
            metadata: AnsightArtifactProviderDescriptor.normalizedMetadata(metadata)
        )
    }

    fileprivate var jsonValue: JSONValue {
        .object([
            "artifactId": .string(artifactId),
            "providerId": .string(providerId),
            "name": .string(name),
            "kind": .string(kind),
            "description": description.map(JSONValue.string) ?? .null,
            "mimeType": .string(mimeType),
            "fileName": .string(fileName),
            "sizeBytes": sizeBytes.map(JSONValue.integer) ?? .null,
            "createdAtUtc": .string(createdAtUtc),
            "tags": .array(tags.map(JSONValue.string)),
            "metadata": .object(metadata.mapValues(JSONValue.string)),
        ])
    }
}

public struct AnsightArtifactPayload: Sendable {
    public let sizeBytes: Int64?
    private let dataProvider: @Sendable () throws -> Data

    public init(sizeBytes: Int64? = nil, dataProvider: @escaping @Sendable () throws -> Data) {
        self.sizeBytes = sizeBytes
        self.dataProvider = dataProvider
    }

    public func readData() throws -> Data {
        try dataProvider()
    }

    public static func fromText(_ text: String, encoding: String.Encoding = .utf8) -> AnsightArtifactPayload {
        AnsightArtifactPayload(sizeBytes: Int64(text.data(using: encoding)?.count ?? 0)) {
            text.data(using: encoding) ?? Data()
        }
    }

    public static func fromBytes(_ bytes: Data) -> AnsightArtifactPayload {
        let captured = bytes
        return AnsightArtifactPayload(sizeBytes: Int64(captured.count)) {
            captured
        }
    }

    public static func fromFile(_ path: String) -> AnsightArtifactPayload {
        AnsightArtifactPayload(sizeBytes: (try? FileManager.default.attributesOfItem(atPath: path)[.size] as? NSNumber)?.int64Value) {
            try Data(contentsOf: URL(fileURLWithPath: path), options: [.mappedIfSafe])
        }
    }
}

public struct AnsightArtifactResult: Sendable {
    public var metadata: AnsightArtifactMetadata
    public var payload: AnsightArtifactPayload

    public init(metadata: AnsightArtifactMetadata, payload: AnsightArtifactPayload) {
        self.metadata = metadata
        self.payload = payload
    }
}

public struct AnsightArtifactQueryContext: Sendable, Equatable {
    public let toolRequestId: String
    public let sessionId: String?
    public let queriedAtUtc: String

    public init(toolRequestId: String, sessionId: String?, queriedAtUtc: String) {
        self.toolRequestId = toolRequestId
        self.sessionId = sessionId
        self.queriedAtUtc = queriedAtUtc
    }
}

public struct AnsightArtifactRequestContext: Sendable, Equatable {
    public let toolRequestId: String
    public let sessionId: String?
    public let requestedAtUtc: String

    public init(toolRequestId: String, sessionId: String?, requestedAtUtc: String) {
        self.toolRequestId = toolRequestId
        self.sessionId = sessionId
        self.requestedAtUtc = requestedAtUtc
    }
}

public struct AnsightArtifactRequest: Sendable, Equatable {
    public let providerId: String
    public let artifactId: String
    public let arguments: [String: String]
    public let context: AnsightArtifactRequestContext

    public init(
        providerId: String,
        artifactId: String,
        arguments: [String: String],
        context: AnsightArtifactRequestContext
    ) {
        self.providerId = providerId
        self.artifactId = artifactId
        self.arguments = arguments
        self.context = context
    }
}

public protocol AnsightArtifactProvider: Sendable {
    var descriptor: AnsightArtifactProviderDescriptor { get }
    func query(context: AnsightArtifactQueryContext) throws -> [AnsightArtifactDefinition]
    func create(request: AnsightArtifactRequest) throws -> AnsightArtifactResult
}

public enum AnsightArtifactToolSupport {
    public static var queryDescriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightArtifactToolIds.query,
            name: "Query Artifacts",
            description: "Queries app-provided artifact providers and currently requestable artifact definitions.",
            category: "artifacts",
            policy: .read,
            keywords: "artifact artifacts query catalog provider export snapshot",
            argumentsSchema: AnsightArtifactToolSchemas.queryArguments,
            resultSchema: AnsightArtifactToolSchemas.queryResult
        )
    }

    public static var requestDescriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightArtifactToolIds.request,
            name: "Request Artifact",
            description: "Requests an app-provided artifact snapshot and streams it to the host.",
            category: "artifacts",
            policy: .read,
            keywords: "artifact artifacts request export snapshot binary stream",
            argumentsSchema: AnsightArtifactToolSchemas.requestArguments,
            resultSchema: AnsightArtifactToolSchemas.requestResult
        )
    }

    public static func executeQuery(
        arguments: [String: String],
        providers: @Sendable () -> [any AnsightArtifactProvider]
    ) throws -> AnsightToolExecutionResult {
        let requestId = AnsightArtifactToolArgumentReader.string(
            arguments,
            key: AnsightToolExecutionArgumentNames.requestId
        ) ?? UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        let sessionId = AnsightArtifactToolArgumentReader.string(
            arguments,
            key: AnsightToolExecutionArgumentNames.sessionId
        )
        let capturedAtUtc = AnsightClock.isoNow()
        let context = AnsightArtifactQueryContext(
            toolRequestId: requestId,
            sessionId: sessionId,
            queriedAtUtc: capturedAtUtc
        )
        let providerFilter = AnsightArtifactToolArgumentReader.string(arguments, key: "providerId")
        let categoryFilter = AnsightArtifactToolArgumentReader.string(arguments, key: "category")
        let kindFilter = AnsightArtifactToolArgumentReader.string(arguments, key: "kind")
        let tagFilter = AnsightArtifactToolArgumentReader.string(arguments, key: "tag")

        var providerArray: [JSONValue] = []
        var artifactArray: [JSONValue] = []

        for provider in providers() {
            do {
                let descriptor = try provider.descriptor.validated()
                if let providerFilter,
                   descriptor.id.caseInsensitiveCompare(providerFilter) != .orderedSame {
                    continue
                }

                let definitions = try provider.query(context: context).map { try $0.validated() }
                providerArray.append(descriptor.jsonValue())

                for definition in definitions where definition.matches(
                    category: categoryFilter,
                    kind: kindFilter,
                    tag: tagFilter
                ) {
                    artifactArray.append(definition.jsonValue(providerId: descriptor.id))
                }
            } catch {
                providerArray.append(provider.descriptor.jsonValue(error: error.localizedDescription))
            }
        }

        return .success(.object([
            "providers": .array(providerArray),
            "artifacts": .array(artifactArray),
            "providerCount": .integer(Int64(providerArray.count)),
            "artifactCount": .integer(Int64(artifactArray.count)),
            "capturedAtUtc": .string(capturedAtUtc),
        ]))
    }

    public static func executeRequest(
        arguments: [String: String],
        providers: @Sendable () -> [any AnsightArtifactProvider],
        runtime: AnsightRuntime
    ) throws -> AnsightToolExecutionResult {
        do {
            guard let requestId = AnsightArtifactToolArgumentReader.string(
                arguments,
                key: AnsightToolExecutionArgumentNames.requestId
            ) else {
                return .failure(
                    "Artifact requests require a live tool protocol request context.",
                    errorCode: "artifact_request_unavailable"
                )
            }

            guard let providerId = AnsightArtifactToolArgumentReader.string(arguments, key: "providerId") else {
                return .failure(
                    "Artifact request must include 'providerId'.",
                    errorCode: "artifact_request_missing_provider_id"
                )
            }

            guard let artifactId = AnsightArtifactToolArgumentReader.string(arguments, key: "artifactId") else {
                return .failure(
                    "Artifact request must include 'artifactId'.",
                    errorCode: "artifact_request_missing_artifact_id"
                )
            }

            guard let provider = try providers().first(where: {
                try $0.descriptor.validated().id.caseInsensitiveCompare(providerId) == .orderedSame
            }) else {
                return .failure(
                    "Artifact provider '\(providerId)' is not registered.",
                    errorCode: "artifact_provider_not_found"
                )
            }

            let requestedAtUtc = AnsightClock.isoNow()
            let sessionId = AnsightArtifactToolArgumentReader.string(
                arguments,
                key: AnsightToolExecutionArgumentNames.sessionId
            )
            let requestArguments = try AnsightArtifactToolArgumentReader.requestArguments(arguments)
            let artifactRequest = AnsightArtifactRequest(
                providerId: providerId,
                artifactId: artifactId,
                arguments: requestArguments,
                context: AnsightArtifactRequestContext(
                    toolRequestId: requestId,
                    sessionId: sessionId,
                    requestedAtUtc: requestedAtUtc
                )
            )

            let result = try provider.create(request: artifactRequest)
            let data = try result.payload.readData()
            var metadata = try result.metadata.validated(expectedProviderId: providerId, expectedArtifactId: artifactId)
            if metadata.sizeBytes == nil {
                metadata.sizeBytes = Int64(data.count)
            }

            let chunkBytes = try AnsightArtifactToolArgumentReader.integer(
                arguments,
                key: "chunkBytes",
                defaultValue: 64 * 1_024,
                minimum: 1_024,
                maximum: 512 * 1_024
            )
            let transferId = UUID()
            let downloadId = AnsightArtifactToolArgumentReader.string(arguments, key: "downloadId") ?? requestId
            let queueResult = runtime.queueBinaryTransfer(
                requestId: requestId,
                transferId: transferId,
                data: data,
                chunkBytes: chunkBytes,
                description: "\(AnsightArtifactToolIds.request):\(providerId):\(artifactId):\(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased())"
            )
            guard queueResult.success else {
                return .failure(
                    queueResult.message,
                    errorCode: "artifact_transfer_unavailable"
                )
            }

            return .success(.object([
                "artifact": metadata.jsonValue,
                "downloadId": .string(downloadId),
                "transferId": .string(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased()),
                "deliveryMode": .string("websocket_binary"),
                "wireProtocol": .string(PairingFileTransferWireProtocol.protocolName),
                "status": .string("queued"),
                "chunkBytes": .integer(Int64(chunkBytes)),
                "capturedAtUtc": .string(requestedAtUtc),
            ]))
        } catch {
            return .failure(error.localizedDescription, errorCode: "artifact_request_failed")
        }
    }
}

private enum AnsightArtifactToolSchemas {
    static let queryArguments = object(
        description: "Arguments for querying app-provided artifacts.",
        properties: [
            "providerId": string("Optional provider id filter.", nullable: true),
            "category": string("Optional artifact category filter.", nullable: true),
            "kind": string("Optional artifact kind filter.", nullable: true),
            "tag": string("Optional artifact tag filter.", nullable: true),
        ]
    )

    static let queryResult = object(
        description: "Artifact catalog payload.",
        properties: [
            "providers": array(genericObject, "Registered artifact providers."),
            "artifacts": array(genericObject, "Available artifact definitions."),
            "providerCount": integer("Number of providers returned."),
            "artifactCount": integer("Number of artifact definitions returned."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["providers", "artifacts", "providerCount", "artifactCount", "capturedAtUtc"]
    )

    static let requestArguments = object(
        description: "Arguments for requesting an app-provided artifact snapshot.",
        properties: [
            "providerId": string("Artifact provider id."),
            "artifactId": string("Artifact id."),
            "downloadId": string("Optional caller-supplied correlation id for mapping the transfer to a host artifact file.", nullable: true),
            "chunkBytes": integer("Maximum bytes to include in each binary WebSocket frame."),
            "arguments": objectJSON("Provider-specific artifact request arguments.", additionalProperties: true, nullable: true),
        ],
        required: ["providerId", "artifactId"]
    )

    static let requestResult = object(
        description: "Requested artifact transfer payload.",
        properties: [
            "artifact": genericObject,
            "downloadId": string("Caller correlation id for the host-side artifact file."),
            "transferId": string("Transfer id carried in the binary frame headers."),
            "deliveryMode": string("How artifact bytes are delivered.", enumValues: ["websocket_binary"]),
            "wireProtocol": string("Binary wire protocol identifier."),
            "status": string("Initial transfer state.", enumValues: ["queued"]),
            "chunkBytes": integer("Maximum bytes per binary frame."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["artifact", "downloadId", "transferId", "deliveryMode", "wireProtocol", "status", "chunkBytes", "capturedAtUtc"]
    )

    private static let genericObject = objectJSON("Generic object.", additionalProperties: true)

    private static func object(
        description: String,
        properties: [String: JSONValue],
        required: [String] = []
    ) -> AnsightToolSchema {
        AnsightToolSchema(json: objectJSON(description, properties: properties, required: required))
    }

    private static func objectJSON(
        _ description: String,
        properties: [String: JSONValue] = [:],
        required: [String] = [],
        additionalProperties: Bool = false,
        nullable: Bool = false
    ) -> JSONValue {
        var object: [String: JSONValue] = [
            "type": nullable ? .array([.string("object"), .string("null")]) : .string("object"),
            "description": .string(description),
            "properties": .object(properties),
            "additionalProperties": .bool(additionalProperties),
        ]
        if !required.isEmpty {
            object["required"] = .array(required.map(JSONValue.string))
        }
        return .object(object)
    }

    private static func array(_ item: JSONValue, _ description: String) -> JSONValue {
        .object([
            "type": .string("array"),
            "description": .string(description),
            "items": item,
        ])
    }

    private static func string(
        _ description: String,
        enumValues: [String]? = nil,
        format: String? = nil,
        nullable: Bool = false
    ) -> JSONValue {
        var object: [String: JSONValue] = [
            "type": nullable ? .array([.string("string"), .string("null")]) : .string("string"),
            "description": .string(description),
        ]
        if let enumValues {
            object["enum"] = .array(enumValues.map(JSONValue.string))
        }
        if let format {
            object["format"] = .string(format)
        }
        return .object(object)
    }

    private static func integer(_ description: String, nullable: Bool = false) -> JSONValue {
        .object([
            "type": nullable ? .array([.string("integer"), .string("null")]) : .string("integer"),
            "description": .string(description),
        ])
    }
}

private enum AnsightArtifactToolArgumentReader {
    static func string(_ arguments: [String: String], key: String) -> String? {
        guard let value = arguments[key]?.trimmingCharacters(in: .whitespacesAndNewlines),
              !value.isEmpty
        else {
            return nil
        }

        return value
    }

    static func integer(
        _ arguments: [String: String],
        key: String,
        defaultValue: Int,
        minimum: Int,
        maximum: Int
    ) throws -> Int {
        guard let text = string(arguments, key: key) else {
            return defaultValue
        }

        guard let value = Int(text) else {
            throw RuntimeError.invalidInput("The argument '\(key)' must be an integer.")
        }

        return min(max(value, minimum), maximum)
    }

    static func requestArguments(_ arguments: [String: String]) throws -> [String: String] {
        var requestArguments: [String: String] = [:]
        for (key, value) in arguments {
            guard !excludedRequestArgumentKeys.contains(key) else {
                continue
            }

            requestArguments[key] = value
        }

        guard let encodedArguments = string(arguments, key: "arguments") else {
            return requestArguments
        }

        guard let data = encodedArguments.data(using: .utf8),
              let object = try JSONSerialization.jsonObject(with: data, options: []) as? [String: Any]
        else {
            throw RuntimeError.invalidInput("The argument 'arguments' must be a JSON object.")
        }

        for (key, value) in object {
            requestArguments[key] = stringValue(from: value)
        }
        return requestArguments
    }

    private static let excludedRequestArgumentKeys: Set<String> = [
        "providerId",
        "artifactId",
        "downloadId",
        "chunkBytes",
        "arguments",
        AnsightToolExecutionArgumentNames.requestId,
        AnsightToolExecutionArgumentNames.sessionId,
    ]

    private static func stringValue(from value: Any) -> String {
        switch value {
        case let value as String:
            return value
        case let value as NSNumber:
            return value.stringValue
        case _ as NSNull:
            return ""
        default:
            guard JSONSerialization.isValidJSONObject(value),
                  let data = try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys]),
                  let string = String(data: data, encoding: .utf8)
            else {
                return String(describing: value)
            }

            return string
        }
    }
}

private extension AnsightArtifactDefinition {
    func matches(category: String?, kind: String?, tag: String?) -> Bool {
        if let category, self.category.caseInsensitiveCompare(category) != .orderedSame {
            return false
        }

        if let kind, self.kind.caseInsensitiveCompare(kind) != .orderedSame {
            return false
        }

        if let tag, !tags.contains(where: { $0.caseInsensitiveCompare(tag) == .orderedSame }) {
            return false
        }

        return true
    }
}

private extension String {
    var nilIfBlank: String? {
        isEmpty ? nil : self
    }
}
