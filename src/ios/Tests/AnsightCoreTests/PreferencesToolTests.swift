import XCTest
@testable import AnsightCore
@testable import AnsightToolsPreferences

final class PreferencesToolTests: XCTestCase {
    func testPreferencesToolsRoundTripViaToolProtocol() throws {
        let suiteName = "ai.ansight.tests.\(UUID().uuidString)"
        let key = "ansight.tests.roundtrip"
        UserDefaults(suiteName: suiteName)?.removePersistentDomain(forName: suiteName)
        defer {
            UserDefaults(suiteName: suiteName)?.removePersistentDomain(forName: suiteName)
        }

        let options = AnsightPreferencesToolOptions(
            defaultStore: suiteName,
            allowedStores: [suiteName],
            allowedKeyPrefixes: ["ansight.tests."]
        )
        let bridge = bridge(
            tools: AnsightPreferencesTools.tools(options: options),
            guardPolicy: .fullAccess
        )

        let setResponse = try call(
            bridge,
            id: "prefs_set",
            toolId: AnsightPreferencesToolIds.setValue,
            arguments: [
                "key": key,
                "value": "hello",
                "valueType": "string",
            ]
        )
        XCTAssertEqual(setResponse.type, "tool.result")

        let getResponse = try call(
            bridge,
            id: "prefs_get",
            toolId: AnsightPreferencesToolIds.getValue,
            arguments: ["key": key]
        )
        let getResult = try resultPayload(getResponse)
        XCTAssertEqual(getResult["store"], .string(suiteName))
        XCTAssertEqual(getResult["key"], .string(key))
        XCTAssertEqual(getResult["exists"], .bool(true))
        XCTAssertEqual(getResult["value"], .string("hello"))
        XCTAssertEqual(getResult["valueType"], .string("string"))

        let listResponse = try call(
            bridge,
            id: "prefs_list",
            toolId: AnsightPreferencesToolIds.listKeys,
            arguments: ["prefix": "ansight.tests."]
        )
        let listResult = try resultPayload(listResponse)
        guard case .array(let keys)? = listResult["keys"] else {
            return XCTFail("Expected key list.")
        }
        XCTAssertTrue(keys.contains(.string(key)))

        let removeResponse = try call(
            bridge,
            id: "prefs_remove",
            toolId: AnsightPreferencesToolIds.removeKey,
            arguments: ["key": key]
        )
        let removeResult = try resultPayload(removeResponse)
        XCTAssertEqual(removeResult["removed"], .bool(true))
    }

    func testPreferencesCatalogIncludesPolicies() throws {
        let bridge = bridge(
            tools: AnsightPreferencesTools.tools(),
            guardPolicy: .fullAccess
        )

        let envelope = try queryCatalog(bridge)
        XCTAssertEqual(envelope.type, "tool.catalog")
        let payload = try decodedToolProtocolPayload(envelope)
        guard case .array(let tools)? = payload["tools"] else {
            return XCTFail("Expected catalog tools.")
        }

        let getValueTool = tools.compactMap { tool -> [String: JSONValue]? in
            guard case .object(let object) = tool,
                  object["id"] == .string(AnsightPreferencesToolIds.getValue) else {
                return nil
            }

            return object
        }.first

        XCTAssertEqual(getValueTool?["policy"], .string("read"))
        XCTAssertNil(getValueTool?["security"])
    }

    func testPreferencesAllowListFailureReturnsToolError() throws {
        let suiteName = "ai.ansight.tests.\(UUID().uuidString)"
        let options = AnsightPreferencesToolOptions(
            defaultStore: suiteName,
            allowedStores: [suiteName],
            allowedKeyPrefixes: ["allowed."]
        )
        let bridge = bridge(
            tools: [GetPreferenceValueTool(options: options)],
            guardPolicy: .fullAccess
        )

        let envelope = try call(
            bridge,
            id: "prefs_denied",
            toolId: AnsightPreferencesToolIds.getValue,
            arguments: ["key": "denied.value"]
        )

        XCTAssertEqual(envelope.type, "tool.error")
        XCTAssertEqual(errorCode(envelope), "prefs_get_failed")
    }

    func testReadOnlyGuardDeniesPreferenceWrites() throws {
        let bridge = bridge(
            tools: [SetPreferenceValueTool()],
            guardPolicy: .readOnly
        )

        let envelope = try call(
            bridge,
            id: "prefs_write_denied",
            toolId: AnsightPreferencesToolIds.setValue,
            arguments: [
                "key": "sample",
                "value": "hello",
                "valueType": "string",
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
            {"type":"tool.query","id":"prefs_catalog","capability":"tool.exec","payload":{}}
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
            sessionId: "prefs_session",
            payload: .object([
                "toolId": .string(toolId),
                "arguments": .object(from: arguments),
            ])
        )
        let json = try JSONEncoder().encode(envelope)
        let request = try XCTUnwrap(String(data: json, encoding: .utf8))
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
