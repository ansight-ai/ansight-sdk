import XCTest
@testable import AnsightKit
@testable import AnsightToolsSecureStorage

final class SecureStorageToolTests: XCTestCase {
    func testSecureStorageToolsRoundTripViaToolProtocol() throws {
        let service = "ai.ansight.tests.secure.\(UUID().uuidString)"
        let key = "ansight.tests.secret"
        let options = AnsightSecureStorageToolsOptions(
            appleService: service,
            allowedKeyPrefixes: ["ansight.tests."]
        )
        let bridge = bridge(
            tools: AnsightSecureStorageTools.tools(options: options),
            guardPolicy: .fullAccess
        )
        defer {
            _ = try? call(
                bridge,
                id: "secure_cleanup",
                toolId: AnsightSecureStorageToolIds.removeKey,
                arguments: ["key": key]
            )
        }

        let setEnvelope = try call(
            bridge,
            id: "secure_set",
            toolId: AnsightSecureStorageToolIds.setValue,
            arguments: [
                "key": key,
                "value": "secret-value",
            ]
        )
        let setResult = try resultPayload(setEnvelope)
        XCTAssertEqual(setResult["store"], .string(service))
        XCTAssertEqual(setResult["key"], .string(key))
        XCTAssertEqual(setResult["updated"], .bool(true))

        let getEnvelope = try call(
            bridge,
            id: "secure_get",
            toolId: AnsightSecureStorageToolIds.getValue,
            arguments: ["key": key]
        )
        let getResult = try resultPayload(getEnvelope)
        XCTAssertEqual(getResult["store"], .string(service))
        XCTAssertEqual(getResult["key"], .string(key))
        XCTAssertEqual(getResult["exists"], .bool(true))
        XCTAssertEqual(getResult["value"], .string("secret-value"))

        let removeEnvelope = try call(
            bridge,
            id: "secure_remove",
            toolId: AnsightSecureStorageToolIds.removeKey,
            arguments: ["key": key]
        )
        let removeResult = try resultPayload(removeEnvelope)
        XCTAssertEqual(removeResult["removed"], .bool(true))

        let missingEnvelope = try call(
            bridge,
            id: "secure_missing",
            toolId: AnsightSecureStorageToolIds.getValue,
            arguments: ["key": key]
        )
        let missingResult = try resultPayload(missingEnvelope)
        XCTAssertEqual(missingResult["exists"], .bool(false))
        XCTAssertEqual(missingResult["value"], .null)
    }

    func testSecureStorageCatalogIncludesCriticalSecurityMetadata() throws {
        let bridge = bridge(
            tools: AnsightSecureStorageTools.tools(
                options: AnsightSecureStorageToolsOptions(allowedKeyPrefixes: ["allowed."])
            ),
            guardPolicy: .fullAccess
        )

        let envelope = try queryCatalog(bridge)
        XCTAssertEqual(envelope.type, "tool.catalog")
        guard case .object(let payload) = envelope.payload,
              case .array(let tools)? = payload["tools"] else {
            return XCTFail("Expected catalog tools.")
        }

        let getTool = tools.compactMap { tool -> [String: JSONValue]? in
            guard case .object(let object) = tool,
                  object["id"] == .string(AnsightSecureStorageToolIds.getValue) else {
                return nil
            }

            return object
        }.first

        guard let getTool,
              case .object(let security)? = getTool["security"],
              case .array(let implications)? = security["implications"] else {
            return XCTFail("Expected get-value security metadata.")
        }

        XCTAssertEqual(security["level"], .string("Critical"))
        XCTAssertTrue(implications.contains(.string("exports_data")))
        XCTAssertTrue(implications.contains(.string("accesses_secure_storage")))
        XCTAssertTrue(implications.contains(.string("handles_secrets")))
    }

    func testSecureStorageDenyByDefaultAndAllowListFailure() throws {
        let bridge = bridge(
            tools: [GetSecureStorageValueTool()],
            guardPolicy: .fullAccess
        )

        let envelope = try call(
            bridge,
            id: "secure_denied",
            toolId: AnsightSecureStorageToolIds.getValue,
            arguments: ["key": "not.allowed"]
        )

        XCTAssertEqual(envelope.type, "tool.error")
        XCTAssertEqual(errorCode(envelope), "secure_get_failed")
    }

    func testReadOnlyGuardDeniesSecureStorageWrites() throws {
        let bridge = bridge(
            tools: [
                SetSecureStorageValueTool(
                    options: AnsightSecureStorageToolsOptions(allowedKeyPrefixes: ["allowed."])
                ),
            ],
            guardPolicy: .readOnly
        )

        let envelope = try call(
            bridge,
            id: "secure_write_denied",
            toolId: AnsightSecureStorageToolIds.setValue,
            arguments: [
                "key": "allowed.key",
                "value": "secret",
            ]
        )

        XCTAssertEqual(envelope.type, "tool.error")
        XCTAssertEqual(errorCode(envelope), "tool_execution_denied")
    }

    private func bridge(tools: [any AnsightTool], guardPolicy: AnsightToolGuard) -> AnsightToolProtocolBridge {
        let registry = Dictionary(
            uniqueKeysWithValues: tools.map { tool in
                (
                    AnsightToolProtocolBridge.normalizedToolId(tool.descriptor.id),
                    RegisteredTool(
                        descriptor: tool.descriptor,
                        execute: { arguments in
                            try tool.execute(arguments: arguments)
                        }
                    )
                )
            }
        )

        return AnsightToolProtocolBridge(registry: registry, guardPolicy: guardPolicy)
    }

    private func queryCatalog(_ bridge: AnsightToolProtocolBridge) throws -> AnsightToolProtocolEnvelope {
        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.query","id":"secure_catalog","capability":"tool.exec","payload":{}}
            """
        )
        return try decodeEnvelope(responseJson)
    }

    private func call(
        _ bridge: AnsightToolProtocolBridge,
        id: String,
        toolId: String,
        arguments: [String: String]
    ) throws -> AnsightToolProtocolEnvelope {
        let envelope = AnsightToolProtocolEnvelope(
            type: "tool.call",
            id: id,
            sessionId: "secure_session",
            payload: .object([
                "toolId": .string(toolId),
                "arguments": .object(from: arguments),
            ])
        )
        let data = try JSONEncoder().encode(envelope)
        let request = try XCTUnwrap(String(data: data, encoding: .utf8))
        let responseJson = try bridge.handleIfSupported(request)
        return try decodeEnvelope(responseJson)
    }

    private func decodeEnvelope(_ json: String?) throws -> AnsightToolProtocolEnvelope {
        let json = try XCTUnwrap(json)
        let data = try XCTUnwrap(json.data(using: .utf8))
        return try JSONDecoder().decode(AnsightToolProtocolEnvelope.self, from: data)
    }

    private func resultPayload(_ envelope: AnsightToolProtocolEnvelope) throws -> [String: JSONValue] {
        guard case .object(let payload) = envelope.payload,
              case .object(let result)? = payload["result"] else {
            XCTFail("Expected tool result payload.")
            return [:]
        }

        return result
    }

    private func errorCode(_ envelope: AnsightToolProtocolEnvelope) -> String? {
        guard case .object(let payload) = envelope.payload,
              case .string(let code)? = payload["code"] else {
            return nil
        }

        return code
    }
}
