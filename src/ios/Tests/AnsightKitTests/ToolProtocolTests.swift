import XCTest
@testable import AnsightKit

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
}

private struct EchoTool: AnsightTool {
    let descriptor = AnsightToolDescriptor(
        id: "echo.tool",
        name: "Echo Tool",
        description: "Echoes a single argument.",
        category: "Diagnostics",
        scope: AnsightToolScope.read.rawValue,
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
        .success(.object([
            "echo": .string(arguments["message"] ?? ""),
            "requestId": .string(arguments[AnsightToolExecutionArgumentNames.requestId] ?? ""),
            "sessionId": .string(arguments[AnsightToolExecutionArgumentNames.sessionId] ?? ""),
        ]))
    }
}
