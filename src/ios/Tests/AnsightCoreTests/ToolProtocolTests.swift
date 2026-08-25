import XCTest
@testable import AnsightCore

final class ToolProtocolTests: XCTestCase {
    func testQueryReturnsCatalogWhenGuardAllowsDiscovery() throws {
        let bridge = AnsightToolProtocolBridge(
            registry: [
                "echo.tool": RegisteredTool(
                    descriptor: EchoTool().descriptor,
                    execute: EchoTool().execute(arguments:)
                ),
            ],
            guardPolicy: .readOnly
        )

        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.query","id":"req_1","sessionId":"sess_1","capability":"tool.exec","payload":{}}
            """
        )

        XCTAssertNotNil(responseJson)
        let envelope = try XCTUnwrap(decodeEnvelope(responseJson))
        XCTAssertEqual(envelope.type, "tool.catalog")
        guard case .object(let payload) = envelope.payload,
              case .integer(let count)? = payload["count"] else {
            return XCTFail("Expected catalog count payload.")
        }

        XCTAssertEqual(count, 1)
    }

    func testQueryCompressesLargeCatalogAndPreservesConditionalRevision() throws {
        let descriptor = AnsightToolDescriptor(
            id: "large.catalog",
            name: "Large Catalog Tool",
            description: String(repeating: "x", count: 64 * 1024),
            category: "Diagnostics"
        )
        let bridge = AnsightToolProtocolBridge(
            registry: [
                "large.catalog": RegisteredTool(
                    descriptor: descriptor,
                    execute: nil
                ),
            ],
            guardPolicy: .readOnly
        )

        let responseJson = try bridge.handleIfSupported(
            #"{"type":"tool.query","id":"large_1","sessionId":"sess_1","capability":"tool.exec","payload":{}}"#
        )
        let envelope = try XCTUnwrap(decodeEnvelope(responseJson))
        guard case .object(let encodedPayload) = envelope.payload,
              case .string(let encoding)? = encodedPayload["$ansightEncoding"],
              case .integer(let originalByteCount)? = encodedPayload["originalByteCount"],
              case .integer(let compressedByteCount)? = encodedPayload["compressedByteCount"] else {
            return XCTFail("Expected encoded catalog payload.")
        }
        XCTAssertEqual(encoding, "gzip-base64-json")
        XCTAssertGreaterThan(originalByteCount, compressedByteCount)

        let decodedPayload = try XCTUnwrap(
            AnsightToolProtocolPayloadEncoding.decodeIfNeeded(envelope.payload)
        )
        guard case .object(let catalogPayload) = decodedPayload,
              case .string(let revision)? = catalogPayload["revision"],
              case .integer(let count)? = catalogPayload["count"],
              case .array(let tools)? = catalogPayload["tools"] else {
            return XCTFail("Expected decoded catalog payload.")
        }
        XCTAssertEqual(count, 1)
        XCTAssertEqual(tools.count, 1)

        let conditionalJson = try bridge.handleIfSupported(
            """
            {"type":"tool.query","id":"large_2","sessionId":"sess_1","capability":"tool.exec","payload":{"ifRevision":"\(revision)"}}
            """
        )
        let conditionalEnvelope = try XCTUnwrap(decodeEnvelope(conditionalJson))
        guard case .object(let conditionalPayload) = conditionalEnvelope.payload,
              case .bool(let unchanged)? = conditionalPayload["unchanged"] else {
            return XCTFail("Expected conditional catalog payload.")
        }
        XCTAssertNil(conditionalPayload["$ansightEncoding"])
        XCTAssertTrue(unchanged)
        XCTAssertNil(conditionalPayload["tools"])
        XCTAssertEqual(conditionalPayload.count, 3)
    }

    func testQuerySupportsCompactIndexAndFocusedDefinitions() throws {
        let route = AnsightToolDescriptor(
            id: "route.open",
            name: "Open Route",
            description: "Open a route."
        )
        let map = AnsightToolDescriptor(
            id: "map.capture",
            name: "Capture Map",
            description: "Capture the current map.",
            prerequisiteToolIds: [route.id]
        )
        let bridge = AnsightToolProtocolBridge(
            registry: [
                route.id: RegisteredTool(descriptor: route, execute: nil),
                map.id: RegisteredTool(descriptor: map, execute: nil),
            ],
            guardPolicy: .readOnly
        )

        let indexJson = try bridge.handleIfSupported(
            #"{"type":"tool.query","id":"index_1","capability":"tool.exec","payload":{"detail":"index","query":"map","limit":1}}"#
        )
        let indexEnvelope = try XCTUnwrap(decodeEnvelope(indexJson))
        guard case .object(let indexPayload) = indexEnvelope.payload,
              case .array(let indexTools)? = indexPayload["tools"],
              case .object(let indexTool) = indexTools.first,
              case .string(let toolId)? = indexTool["id"],
              case .array(let prerequisites)? = indexTool["prerequisiteToolIds"],
              case .string(let prerequisiteId) = prerequisites.first else {
            return XCTFail("Expected a focused compact index entry.")
        }
        XCTAssertEqual(toolId, map.id)
        XCTAssertEqual(prerequisiteId, route.id)
        XCTAssertNil(indexTool["argumentsSchema"])
        XCTAssertNotNil(indexTool["definitionRevision"])

        let definitionJson = try bridge.handleIfSupported(
            #"{"type":"tool.query","id":"definitions_1","capability":"tool.exec","payload":{"detail":"definitions","ids":["map.capture"]}}"#
        )
        let definitionEnvelope = try XCTUnwrap(decodeEnvelope(definitionJson))
        guard case .object(let definitionPayload) = definitionEnvelope.payload,
              case .array(let definitions)? = definitionPayload["tools"],
              case .object(let definition) = definitions.first else {
            return XCTFail("Expected a focused tool definition.")
        }
        XCTAssertNotNil(definition["argumentsSchema"])
        XCTAssertNil(definition["runtime"])
    }

    func testCallExecutesRegisteredTool() throws {
        let bridge = AnsightToolProtocolBridge(
            registry: [
                "echo.tool": RegisteredTool(
                    descriptor: EchoTool().descriptor,
                    execute: EchoTool().execute(arguments:)
                ),
            ],
            guardPolicy: .readOnly
        )

        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.call","id":"req_2","sessionId":"sess_1","capability":"tool.exec","payload":{"toolId":"echo.tool","arguments":{"message":"hello"}}}
            """
        )

        XCTAssertNotNil(responseJson)
        let envelope = try XCTUnwrap(decodeEnvelope(responseJson))
        XCTAssertEqual(envelope.type, "tool.result")
        guard case .object(let payload) = envelope.payload,
              case .object(let result)? = payload["result"],
              case .string(let echoed)? = result["echo"],
              case .string(let requestId)? = result["requestId"],
              case .string(let sessionId)? = result["sessionId"] else {
            return XCTFail("Expected successful tool result payload.")
        }

        XCTAssertEqual(echoed, "hello")
        XCTAssertEqual(requestId, "req_2")
        XCTAssertEqual(sessionId, "sess_1")
    }

    func testCatalogAndCallReportRuntimePreconditions() throws {
        let bridge = AnsightToolProtocolBridge(
            registry: [
                "echo.tool": RegisteredTool(
                    descriptor: EchoTool().descriptor,
                    availability: { _ in
                        .unavailable(
                            reasonCode: "screen_not_registered",
                            reason: "No active MapWorkScreen is registered.",
                            requiredState: "MapWorkScreen registered",
                            remediation: "Navigate to the map screen and retry."
                        )
                    },
                    execute: EchoTool().execute(arguments:)
                ),
            ],
            guardPolicy: .readOnly
        )

        let catalogJson = try bridge.handleIfSupported(
            #"{"type":"tool.query","id":"query_1","sessionId":"sess_1","capability":"tool.exec","payload":{}}"#
        )
        let catalog = try XCTUnwrap(decodeEnvelope(catalogJson))
        guard case .object(let catalogPayload) = catalog.payload,
              case .array(let tools)? = catalogPayload["tools"],
              case .object(let entry) = tools.first,
              case .bool(let executable)? = entry["executable"],
              case .object(let runtime)? = entry["runtime"],
              case .string(let reasonCode)? = runtime["code"] else {
            return XCTFail("Expected runtime availability in the tool catalog.")
        }
        XCTAssertFalse(executable)
        XCTAssertEqual(reasonCode, "screen_not_registered")

        let callJson = try bridge.handleIfSupported(
            #"{"type":"tool.call","id":"call_1","sessionId":"sess_1","capability":"tool.exec","payload":{"toolId":"echo.tool","arguments":{}}}"#
        )
        let call = try XCTUnwrap(decodeEnvelope(callJson))
        XCTAssertEqual(call.type, "tool.error")
        XCTAssertEqual(errorCode(in: call), "screen_not_registered")
    }

    func testCallPreservesJsonArgumentsAsStrings() throws {
        let bridge = AnsightToolProtocolBridge(
            registry: [
                "echo.tool": RegisteredTool(
                    descriptor: EchoTool().descriptor,
                    execute: EchoTool().execute(arguments:)
                ),
            ],
            guardPolicy: .readOnly
        )

        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.call","id":"req_json","capability":"tool.exec","payload":{"toolId":"echo.tool","arguments":{"count":3,"enabled":true,"filter":{"status":"open"},"ids":[1,2],"none":null}}}
            """
        )

        let envelope = try XCTUnwrap(decodeEnvelope(responseJson))
        XCTAssertEqual(envelope.type, "tool.result")
        guard case .object(let payload) = envelope.payload,
              case .object(let result)? = payload["result"],
              case .string(let count)? = result["count"],
              case .string(let enabled)? = result["enabled"],
              case .string(let filter)? = result["filter"],
              case .string(let ids)? = result["ids"] else {
            return XCTFail("Expected stringified JSON arguments.")
        }

        XCTAssertEqual(count, "3")
        XCTAssertEqual(enabled, "true")
        XCTAssertEqual(filter, #"{"status":"open"}"#)
        XCTAssertEqual(ids, "[1,2]")
        XCTAssertNil(result["none"])
    }

    func testCallResolvesToolIdsCaseInsensitively() throws {
        let bridge = AnsightToolProtocolBridge(
            registry: [
                AnsightToolProtocolBridge.normalizedToolId("Echo.Tool"): RegisteredTool(
                    descriptor: EchoTool().descriptor,
                    execute: EchoTool().execute(arguments:)
                ),
            ],
            guardPolicy: .readOnly
        )

        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.call","id":"req_case","capability":"tool.exec","payload":{"toolId":"ECHO.TOOL","arguments":{"message":"hello"}}}
            """
        )

        let envelope = try XCTUnwrap(decodeEnvelope(responseJson))
        XCTAssertEqual(envelope.type, "tool.result")
    }

    func testInvalidToolProtocolRequestReturnsToolError() throws {
        let bridge = AnsightToolProtocolBridge(registry: [:], guardPolicy: .readOnly)

        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.call","capability":"tool.exec","payload":{"toolId":"echo.tool"}}
            """
        )

        let envelope = try XCTUnwrap(decodeEnvelope(responseJson))
        XCTAssertEqual(envelope.type, "tool.error")
        guard case .object(let payload) = envelope.payload,
              case .string(let code)? = payload["code"] else {
            return XCTFail("Expected tool error payload.")
        }
        XCTAssertEqual(code, "tool_protocol_invalid_request")
    }

    func testUnsupportedToolCapabilityIsIgnored() throws {
        let bridge = AnsightToolProtocolBridge(registry: [:], guardPolicy: .readOnly)

        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.query","id":"req_wrong_capability","capability":"tool.other","payload":{}}
            """
        )

        XCTAssertNil(responseJson)
    }

    func testUnknownToolProtocolTypeReturnsToolError() throws {
        let bridge = AnsightToolProtocolBridge(registry: [:], guardPolicy: .readOnly)

        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.unknown","id":"req_unknown","capability":"tool.exec","payload":{}}
            """
        )

        let envelope = try XCTUnwrap(decodeEnvelope(responseJson))
        XCTAssertEqual(envelope.type, "tool.error")
        XCTAssertEqual(errorCode(in: envelope), "tool_protocol_unknown_type")
        XCTAssertEqual(envelope.replyTo, "req_unknown")
    }

    func testInvalidToolCallPayloadReturnsToolError() throws {
        let bridge = AnsightToolProtocolBridge(registry: [:], guardPolicy: .readOnly)

        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.call","id":"req_bad_payload","capability":"tool.exec","payload":["not","an","object"]}
            """
        )

        let envelope = try XCTUnwrap(decodeEnvelope(responseJson))
        XCTAssertEqual(envelope.type, "tool.error")
        XCTAssertEqual(errorCode(in: envelope), "tool_call_payload_invalid")
        XCTAssertEqual(envelope.replyTo, "req_bad_payload")
    }

    func testInvalidToolCallArgumentsReturnToolError() throws {
        let bridge = AnsightToolProtocolBridge(
            registry: [
                "echo.tool": RegisteredTool(
                    descriptor: EchoTool().descriptor,
                    execute: EchoTool().execute(arguments:)
                ),
            ],
            guardPolicy: .readOnly
        )

        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.call","id":"req_bad_args","capability":"tool.exec","payload":{"toolId":"echo.tool","arguments":["not","an","object"]}}
            """
        )

        let envelope = try XCTUnwrap(decodeEnvelope(responseJson))
        XCTAssertEqual(envelope.type, "tool.error")
        guard case .object(let payload) = envelope.payload,
              case .string(let code)? = payload["code"] else {
            return XCTFail("Expected tool error payload.")
        }
        XCTAssertEqual(code, "tool_call_arguments_invalid")
    }

    func testRuntimeRejectsDuplicateToolIdsCaseInsensitively() throws {
        let id = "duplicate.\(UUID().uuidString)"
        try AnsightRuntime.shared.initialize(
            options: AnsightOptions(hostAutoProbe: .disabledDefault)
        )
        try AnsightRuntime.shared.registerTool(
            AnsightToolDescriptor(id: id, name: "First")
        )

        XCTAssertThrowsError(
            try AnsightRuntime.shared.registerTool(
                AnsightToolDescriptor(id: id.uppercased(), name: "Second")
            )
        )
    }

    private func decodeEnvelope(_ json: String?) -> AnsightToolProtocolEnvelope? {
        guard let json, let data = json.data(using: .utf8) else {
            return nil
        }

        return try? JSONDecoder().decode(AnsightToolProtocolEnvelope.self, from: data)
    }

    private func errorCode(in envelope: AnsightToolProtocolEnvelope) -> String? {
        guard case .object(let payload) = envelope.payload,
              case .string(let code)? = payload["code"] else {
            return nil
        }

        return code
    }
}

private struct EchoTool: AnsightTool {
    let descriptor = AnsightToolDescriptor(
        id: "echo.tool",
        name: "Echo Tool",
        description: "Echoes a single argument.",
        category: "Diagnostics",
        policy: .read,
        keywords: "echo",
        argumentsSchema: AnsightToolSchema(
            json: .object([
                "type": .string("object"),
            ])
        ),
        resultSchema: AnsightToolSchema(
            json: .object([
                "type": .string("object"),
            ])
        )
    )

    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        var result: [String: JSONValue] = [
            "echo": .string(arguments["message"] ?? ""),
            "requestId": .string(arguments[AnsightToolExecutionArgumentNames.requestId] ?? ""),
            "sessionId": .string(arguments[AnsightToolExecutionArgumentNames.sessionId] ?? ""),
        ]

        for key in ["count", "enabled", "filter", "ids"] {
            if let value = arguments[key] {
                result[key] = .string(value)
            }
        }

        return .success(.object(result))
    }
}
