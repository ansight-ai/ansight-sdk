import XCTest
@testable import AnsightCore
@testable import AnsightToolsVisualTree

final class VisualTreeToolTests: XCTestCase {
    func testVisualNodeIncludesAutomationIdentifier() {
        let node = AnsightVisualNode(
            id: "node-1",
            type: "UIButton",
            automationId: "checkout.submit",
            label: "Submit",
            role: "button",
            supportedActions: ["tap"],
            visible: true,
            enabled: true,
            focusable: true,
            bounds: nil,
            visual: [:],
            z: 4,
            properties: [:],
            children: []
        )

        let typeRegistry = AnsightVisualTreeTypeRegistry()
        guard case .object(let payload) = node.jsonValue(
            includeBounds: true,
            includeProperties: true,
            maxDepth: 1,
            typeRegistry: typeRegistry
        ) else {
            return XCTFail("Expected a visual-tree node object.")
        }
        XCTAssertEqual(payload["automationId"], .string("checkout.submit"))
        XCTAssertEqual(payload["typeId"], .integer(0))
        XCTAssertNil(payload["type"])
        XCTAssertEqual(typeRegistry.jsonValue, .array([.string("UIButton")]))
        XCTAssertEqual(payload["z"], .number(4))
    }

    func testVisualTreeToolsRegisterExpectedToolIds() {
        XCTAssertEqual(
            [
                AnsightVisualTreeToolIds.getVisualTree,
                AnsightVisualTreeToolIds.getScreenshot,
                AnsightVisualTreeToolIds.inspectNode,
                AnsightVisualTreeToolIds.showOverlay,
                AnsightVisualTreeToolIds.getOverlay,
                AnsightVisualTreeToolIds.queryOverlays,
                AnsightVisualTreeToolIds.updateOverlay,
                AnsightVisualTreeToolIds.removeOverlay,
                AnsightVisualTreeToolIds.clearOverlays,
            ],
            AnsightVisualTreeTools.tools().map(\.descriptor.id)
        )
    }

    func testVisualTreeCatalogIncludesSecurityMetadata() throws {
        let bridge = bridge(
            tools: AnsightVisualTreeTools.tools(),
            guardPolicy: .fullAccess
        )

        let envelope = try queryCatalog(bridge)
        XCTAssertEqual(envelope.type, "tool.catalog")
        guard case .object(let payload) = envelope.payload,
              case .array(let tools)? = payload["tools"] else {
            return XCTFail("Expected catalog tools.")
        }

        XCTAssertEqual(payload["count"], .integer(9))
        let screenshotTool = tools.compactMap { tool -> [String: JSONValue]? in
            guard case .object(let object) = tool,
                  object["id"] == .string(AnsightVisualTreeToolIds.getScreenshot) else {
                return nil
            }

            return object
        }.first

        guard let screenshotTool,
              case .object(let security)? = screenshotTool["security"],
              case .array(let implications)? = security["implications"] else {
            return XCTFail("Expected screenshot security metadata.")
        }

        XCTAssertEqual(security["level"], .string("High"))
        XCTAssertTrue(implications.contains(.string("captures_screenshots")))
        XCTAssertTrue(implications.contains(.string("uses_binary_transfer")))
    }

    func testVisualTreeToolsReturnPlatformUnsupportedOnHost() throws {
        #if canImport(UIKit)
        throw XCTSkip("UIKit-backed visual tree tools require an active app window.")
        #else
        let cases: [(any AnsightTool, [String: String])] = [
            (GetVisualTreeTool(), [:]),
            (GetScreenshotTool(), [:]),
            (InspectNodeTool(), ["nodeId": "root"]),
            (ShowOverlayTool(), ["x": "0", "y": "0", "width": "100", "height": "100"]),
            (GetOverlayTool(), ["overlayId": "overlay-1"]),
            (QueryOverlaysTool(), [:]),
            (UpdateOverlayTool(), ["overlayId": "overlay-1", "strokeColor": "blue"]),
            (RemoveOverlayTool(), ["overlayId": "overlay-1"]),
            (ClearOverlaysTool(), [:]),
        ]

        for (tool, arguments) in cases {
            let result = try tool.execute(arguments: arguments)
            XCTAssertFalse(result.success, "Expected \(tool.descriptor.id) to fail on host platform.")
            XCTAssertEqual(result.errorCode, "visual_tree_platform_unsupported")
        }
        #endif
    }

    func testVisualTreeProviderRegistryRoutesSourceRequests() throws {
        try AnsightVisualTreeProviderRegistry.register(StubVisualTreeProvider(source: "unit-test"))

        let treeResult = try GetVisualTreeTool().execute(arguments: ["source": "unit-test"])
        XCTAssertTrue(treeResult.success)
        XCTAssertEqual(resultObject(treeResult)?["source"], .string("unit-test"))
        XCTAssertEqual(resultObject(treeResult)?["adapter"], .string("unit.test"))

        let inspectResult = try InspectNodeTool().execute(arguments: [
            "source": "unit-test",
            "nodeId": "root",
        ])
        XCTAssertTrue(inspectResult.success)
        XCTAssertEqual(resultObject(inspectResult)?["source"], .string("unit-test"))
        XCTAssertEqual(resultObject(inspectResult)?["node"], .object([
            "id": .string("root"),
            "type": .string("TestNode"),
        ]))
    }

    func testReadOnlyGuardDeniesOverlayWrites() throws {
        let bridge = bridge(
            tools: [ShowOverlayTool()],
            guardPolicy: .readOnly
        )

        let envelope = try call(
            bridge,
            id: "overlay_write_denied",
            toolId: AnsightVisualTreeToolIds.showOverlay,
            arguments: [
                "x": "0",
                "y": "0",
                "width": "100",
                "height": "100",
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
            {"type":"tool.query","id":"visual_catalog","capability":"tool.exec","payload":{}}
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
        let argumentsJson = try JSONValue.object(from: arguments).jsonString()
        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.call","id":"\(id)","capability":"tool.exec","payload":{"toolId":"\(toolId)","arguments":\(argumentsJson)}}
            """
        )
        return try decodeEnvelope(responseJson)
    }

    private func decodeEnvelope(_ json: String?) throws -> AnsightToolProtocolEnvelope {
        guard let json, let data = json.data(using: .utf8) else {
            throw XCTSkip("Expected bridge response JSON.")
        }

        return try JSONDecoder().decode(AnsightToolProtocolEnvelope.self, from: data)
    }

    private func errorCode(_ envelope: AnsightToolProtocolEnvelope) -> String? {
        guard case .object(let payload) = envelope.payload,
              case .string(let code)? = payload["code"] else {
            return nil
        }

        return code
    }

    private func resultObject(_ result: AnsightToolExecutionResult) -> [String: JSONValue]? {
        guard case .object(let object)? = result.result else {
            return nil
        }

        return object
    }
}

private struct StubVisualTreeProvider: AnsightVisualTreeProvider {
    let source: String
    let displayName = "Unit Test"

    func getVisualTree(arguments: [String: String]) -> AnsightToolExecutionResult {
        .success(.object([
            "platform": .string("test"),
            "source": .string(source),
            "adapter": .string("unit.test"),
            "capturedAtUtc": .string("2026-06-16T00:00:00.000Z"),
            "root": .object([
                "id": .string("root"),
                "type": .string("TestNode"),
            ]),
        ]))
    }

    func inspectNode(arguments: [String: String]) -> AnsightToolExecutionResult {
        .success(.object([
            "platform": .string("test"),
            "source": .string(source),
            "adapter": .string("unit.test"),
            "capturedAtUtc": .string("2026-06-16T00:00:00.000Z"),
            "node": .object([
                "id": .string(arguments["nodeId"] ?? "root"),
                "type": .string("TestNode"),
            ]),
        ]))
    }
}
