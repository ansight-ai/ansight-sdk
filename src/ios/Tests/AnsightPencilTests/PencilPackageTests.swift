import XCTest
import AnsightCore
import AnsightPencil
import AnsightPencilRuntime

final class PencilPackageTests: XCTestCase {
    func testBuilderIsPassiveAndDoesNotDuplicateSetup() throws {
        let options = try AnsightOptions.createBuilder().withReadOnlyToolAccess()
            .withSimulatorPenInput().withSimulatorPenInput().build()
        XCTAssertEqual(options.plugins.map(\.id), ["ansight.pencil"])
        XCTAssertEqual(options.toolGuard, .readOnly)
    }

    func testCoreOptionsDoNotContainSimulationActivation() throws {
        let options = AnsightOptions()
        let json = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(options)) as? [String: Any])
        XCTAssertNil(json["enableSimulatorPenInput"])
        XCTAssertEqual(options.toolGuard, .unconfigured)
    }

    func testUnsupportedPlatformHasNoToolsAndRejectsNativePlayback() throws {
#if !targetEnvironment(simulator)
        XCTAssertFalse(AnsightPencil.isSupported)
        XCTAssertTrue(AnsightPencil.tools().isEmpty)
        XCTAssertFalse(try AnsightPencil.register())
        XCTAssertFalse(try AnsightPencil.register())
        XCTAssertFalse(AnsightRuntime.shared.isToolRegistered("ui.simulate_pen"))
        let result = try JSONDecoder().decode(AnsightToolExecutionResult.self,
            from: Data(ANSPencilRuntime.execute(argumentsJson: "{}").utf8))
        XCTAssertFalse(result.success)
        XCTAssertEqual(result.errorCode, "pen_unavailable")
#endif
    }
}
