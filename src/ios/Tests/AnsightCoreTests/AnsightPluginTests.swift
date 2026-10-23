import XCTest
import AnsightCore

final class AnsightPluginTests: XCTestCase {
    private enum SetupError: Error { case pluginFailed }

    func testBuilderIsPassivePreservesPluginsAndDoesNotEncodeCallbacks() throws {
        let plugin = AnsightPlugin(id: "optional") { _ in throw SetupError.pluginFailed }
        let builder = AnsightOptions.createBuilder().withPlugin(plugin).withPlugin(plugin)
        let first = try builder.build()
        let second = try AnsightOptions.createBuilder(first).withReadWriteToolAccess().build()
        XCTAssertEqual(second.plugins, [plugin])
        XCTAssertEqual(second.toolGuard, .readWrite)
        XCTAssertEqual(first.plugins, [plugin])
        let data = try JSONEncoder().encode(second)
        let payload = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        XCTAssertNil(payload["plugins"])
        XCTAssertTrue(try JSONDecoder().decode(AnsightOptions.self, from: data).plugins.isEmpty)
    }

    func testInitializationCallsPluginAfterRuntimeSetupAndPropagatesFailure() throws {
        let options = try AnsightOptions.createBuilder()
            .withoutHostAutoProbe().withoutFramesPerSecond().withoutTouchCapture()
            .withoutCrashCapture().withoutNetworkCapture()
            .withReadWriteToolAccess()
            .withPlugin(AnsightPlugin(id: "optional") { runtime in
                XCTAssertTrue(runtime.snapshot().initialized)
                XCTAssertEqual(runtime.currentOptions().toolGuard, .readWrite)
                throw SetupError.pluginFailed
            }).build()
        XCTAssertThrowsError(try AnsightRuntime.shared.initialize(options: options)) { error in
            XCTAssertTrue(error is SetupError)
        }
    }
}
