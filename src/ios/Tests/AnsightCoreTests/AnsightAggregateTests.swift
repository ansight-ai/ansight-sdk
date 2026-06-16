import XCTest
@testable import Ansight
@testable import AnsightCore
@testable import AnsightToolsDatabase
@testable import AnsightToolsFileSystem
@testable import AnsightToolsPreferences
@testable import AnsightToolsSecureStorage
@testable import AnsightToolsVisualTree

final class AnsightAggregateTests: XCTestCase {
    func testDeveloperDefaultsMatchAllInOneRuntimeDefaults() throws {
        let options = try AnsightOptions.ansightDeveloperDefaults.validated()

        XCTAssertEqual(options.sampleFrequencyMilliseconds, 400)
        XCTAssertEqual(options.retentionPeriodSeconds, 120)
        XCTAssertEqual(options.enableFramesPerSecond, true)
        XCTAssertEqual(options.toolGuard, .fullAccess)
        XCTAssertEqual(options.hostAutoProbe.enabled, true)
        XCTAssertEqual(options.sessionJpegCapture?.intervalMilliseconds, 2_000)
        XCTAssertEqual(options.sessionJpegCapture?.quality, 60)
        XCTAssertEqual(options.sessionJpegCapture?.maxWidth, 480)
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
}
