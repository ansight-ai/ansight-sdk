import XCTest
@testable import AnsightCore

final class ScopedToolGrantTests: XCTestCase {
    private func descriptor(_ id: String = "test.pencil", _ policy: AnsightToolPolicy = .write) -> AnsightToolDescriptor {
        AnsightToolDescriptor(id: id, name: id, description: "Permission test", category: "test", policy: policy)
    }

    func testGrantAllowsOnlyExactToolAndPolicyWithoutChangingPreset() throws {
        let guardPolicy = AnsightToolGuard.readOnly.granting("test.pencil", through: .write)
        XCTAssertTrue(guardPolicy.isVisible(descriptor()))
        XCTAssertNil(guardPolicy.executionDenialReason(for: descriptor()))
        XCTAssertNotNil(guardPolicy.executionDenialReason(for: descriptor("test.other")))
        XCTAssertNotNil(guardPolicy.executionDenialReason(for: descriptor("test.pencil.child")))
        XCTAssertNotNil(guardPolicy.executionDenialReason(for: descriptor("test.pencil", .critical)))
        XCTAssertNotNil(AnsightToolGuard.readOnly.executionDenialReason(for: descriptor()))
        XCTAssertEqual(guardPolicy.maxPolicy, .read)
        XCTAssertEqual(try JSONDecoder().decode(AnsightToolGuard.self, from: JSONEncoder().encode(guardPolicy)), guardPolicy)
    }

    func testUnconfiguredDefaultGrantsOnlySelectedToolButExplicitDisableWins() throws {
        let guardPolicy = AnsightOptions().toolGuard.granting("test.pencil", through: .write)
        XCTAssertNil(guardPolicy.executionDenialReason(for: descriptor()))
        XCTAssertFalse(guardPolicy.isVisible(descriptor("test.other", .read)))
        for disabled in [AnsightToolGuard.disabled,
                         AnsightToolGuard(discoveryEnabled: true, executionEnabled: false, maxPolicy: .read)] {
            XCTAssertNotNil(disabled.granting("test.pencil", through: .write).executionDenialReason(for: descriptor()))
        }
        XCTAssertFalse(AnsightToolGuard.disabled.granting("test.pencil", through: .write).isVisible(descriptor()))
    }

    func testProtocolUsesScopedGrantForDiscoveryAndExecution() throws {
        let allowed = RegisteredTool(descriptor: descriptor(), execute: { _ in .success() })
        let denied = RegisteredTool(descriptor: descriptor("test.other"), execute: { _ in
            XCTFail("Unrelated write executed"); return .success()
        })
        let bridge = AnsightToolProtocolBridge(registry: ["test.pencil": allowed, "test.other": denied],
            guardPolicy: .readOnly.granting("test.pencil", through: .write))
        let catalog = try XCTUnwrap(bridge.handleIfSupported(#"{"type":"tool.query","id":"catalog","payload":{}}"#))
        XCTAssertTrue(catalog.contains("test.pencil"))
        XCTAssertFalse(catalog.contains("test.other"))
        let allowedResponse = try XCTUnwrap(bridge.handleIfSupported(#"{"type":"tool.call","id":"allowed","payload":{"toolId":"test.pencil","arguments":{}}}"#))
        XCTAssertTrue(allowedResponse.contains("tool.result"))
        let deniedResponse = try XCTUnwrap(bridge.handleIfSupported(#"{"type":"tool.call","id":"denied","payload":{"toolId":"test.other","arguments":{}}}"#))
        XCTAssertTrue(deniedResponse.contains("tool_execution_denied"))
    }
}
