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
              case .string(let echoed)? = result["echo"] else {
            return XCTFail("Expected successful tool result payload.")
        }

        XCTAssertEqual(echoed, "hello")
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
        ]))
    }
}
