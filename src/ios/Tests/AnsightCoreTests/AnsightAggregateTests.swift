import XCTest
@testable import Ansight
@testable import AnsightCore
@testable import AnsightObjC
@testable import AnsightToolsDatabase
@testable import AnsightToolsFileSystem
@testable import AnsightToolsPreferences
@testable import AnsightToolsReflection
@testable import AnsightToolsSecureStorage
@testable import AnsightToolsVisualTree

final class AnsightAggregateTests: XCTestCase {
    func testDeveloperDefaultsMatchAllInOneRuntimeDefaults() throws {
        let options = try AnsightOptions.ansightDeveloperDefaults.validated()

        XCTAssertEqual(options.sampleFrequencyMilliseconds, 400)
        XCTAssertEqual(options.retentionPeriodSeconds, 120)
        XCTAssertEqual(options.enableFramesPerSecond, true)
        XCTAssertEqual(options.toolGuard, .readOnly)
        XCTAssertEqual(options.hostAutoProbe.enabled, true)
        XCTAssertEqual(options.sessionJpegCapture?.intervalMilliseconds, 2_000)
        XCTAssertEqual(options.sessionJpegCapture?.quality, 60)
        XCTAssertEqual(options.sessionJpegCapture?.maxWidth, 480)
        XCTAssertEqual(options.sessionJpegCapture?.captureGpuBackedSurfaces, true)
        XCTAssertNotNil(options.touchCapture)
    }

    func testAggregateRemoteToolsIncludesCurrentNativeSuites() {
        let toolIds = AnsightRemoteTools.tools().map(\.descriptor.id).sorted()

        XCTAssertEqual(
            toolIds,
            [
                AnsightDatabaseToolIds.describeSchema,
                AnsightDatabaseToolIds.listDatabases,
                AnsightDatabaseToolIds.query,
                AnsightFileSystemToolIds.beginBinaryDownload,
                AnsightFileSystemToolIds.copyFile,
                AnsightFileSystemToolIds.deleteFile,
                AnsightFileSystemToolIds.downloadFile,
                AnsightFileSystemToolIds.getFileChecksum,
                AnsightFileSystemToolIds.listDirectory,
                AnsightFileSystemToolIds.moveFile,
                AnsightFileSystemToolIds.pushFile,
                AnsightFileSystemToolIds.readFile,
                AnsightPreferencesToolIds.getValue,
                AnsightPreferencesToolIds.listKeys,
                AnsightPreferencesToolIds.removeKey,
                AnsightPreferencesToolIds.setValue,
                AnsightReflectionToolIds.describeType,
                AnsightReflectionToolIds.inspectObject,
                AnsightReflectionToolIds.invokeMethod,
                AnsightReflectionToolIds.listRoots,
                AnsightReflectionToolIds.setMemberValue,
                AnsightSecureStorageToolIds.getValue,
                AnsightSecureStorageToolIds.removeKey,
                AnsightSecureStorageToolIds.setValue,
                AnsightVisualTreeToolIds.clearOverlays,
                AnsightVisualTreeToolIds.getOverlay,
                AnsightVisualTreeToolIds.getScreenshot,
                AnsightVisualTreeToolIds.getVisualTree,
                AnsightVisualTreeToolIds.inspectNode,
                AnsightVisualTreeToolIds.queryOverlays,
                AnsightVisualTreeToolIds.removeOverlay,
                AnsightVisualTreeToolIds.showOverlay,
                AnsightVisualTreeToolIds.updateOverlay,
            ].sorted()
        )
    }

    func testAggregateRemoteToolsCanDisableVisualTreeSuite() {
        let toolIds = AnsightRemoteTools.tools(
            options: AnsightRemoteToolOptions(visualTree: false)
        )
        .map(\.descriptor.id)

        XCTAssertFalse(toolIds.contains(AnsightVisualTreeToolIds.getVisualTree))
        XCTAssertFalse(toolIds.contains(AnsightVisualTreeToolIds.getScreenshot))
        XCTAssertTrue(toolIds.contains(AnsightDatabaseToolIds.listDatabases))
    }

    func testObjCFacadeInitializesAndSamplesMetricStream() throws {
        try ANSAnsight.initializeAndActivate(pairingConfigJson: nil, clientName: "ObjC Unit Test")
        defer {
            ANSAnsight.deactivate()
        }

        try ANSAnsight.registerMetricStream(
            id: 91,
            name: "React Native JS FPS",
            unit: "fps",
            type: "reactNative",
            colorHex: "#61DAFB"
        ) {
            NSNumber(value: 58)
        }

        AnsightRuntime.shared.captureBuiltInTelemetrySample()

        XCTAssertTrue(ANSAnsight.isInitialized)
        XCTAssertTrue(ANSAnsight.isActive)
        XCTAssertEqual(AnsightRuntime.shared.recordedMetrics().last?.channel, 91)
        XCTAssertEqual(AnsightRuntime.shared.recordedMetrics().last?.value, 58)
        XCTAssertEqual(AnsightRuntime.shared.snapshot().channels.first { $0.id == 91 }?.unit, "fps")
        XCTAssertEqual(AnsightRuntime.shared.snapshot().channels.first { $0.id == 91 }?.type, "reactNative")
    }

    func testObjCFacadeRegistersVisualTreeProvider() throws {
        let provider = ANSVisualTreeProvider(
            source: "objc-test",
            displayName: "ObjC Test",
            getVisualTree: { _ in
                [
                    "platform": "test",
                    "source": "objc-test",
                    "adapter": "objc.test",
                    "capturedAtUtc": "2026-06-16T00:00:00.000Z",
                    "root": [
                        "id": "root",
                        "type": "ObjCTestNode",
                    ],
                ] as NSDictionary
            },
            inspectNode: { arguments in
                [
                    "platform": "test",
                    "source": "objc-test",
                    "adapter": "objc.test",
                    "capturedAtUtc": "2026-06-16T00:00:00.000Z",
                    "node": [
                        "id": arguments["nodeId"] as? String ?? "root",
                        "type": "ObjCTestNode",
                    ],
                ] as NSDictionary
            }
        )

        try ANSAnsight.registerVisualTreeProvider(provider)

        let result = try GetVisualTreeTool().execute(arguments: ["source": "objc-test"])
        XCTAssertTrue(result.success)
        XCTAssertEqual(resultObject(result)?["source"], .string("objc-test"))
        XCTAssertEqual(resultObject(result)?["adapter"], .string("objc.test"))
        XCTAssertTrue(ANSAnsight.registeredVisualTreeSources().contains("objc-test"))
    }

    private func resultObject(_ result: AnsightToolExecutionResult) -> [String: JSONValue]? {
        guard case .object(let object)? = result.result else {
            return nil
        }

        return object
    }
}
