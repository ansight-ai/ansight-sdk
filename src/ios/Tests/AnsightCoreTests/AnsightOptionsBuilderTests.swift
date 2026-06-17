import XCTest
@testable import AnsightCore

final class AnsightOptionsBuilderTests: XCTestCase {
    func testBuilderAppliesDotNetStyleOptionsConvention() throws {
        let options = try AnsightOptions.createBuilder()
            .withSampleFrequencyMilliseconds(400)
            .withRetentionPeriodSeconds(120)
            .withoutFramesPerSecond()
            .withBatteryLevel()
            .withDefaultMemoryChannels(.all)
            .withoutDefaultMemoryChannels(.nativeHeap)
            .withSessionJpegCapture()
            .withTouchCapture(moveCaptureFramesPerSecond: 12)
            .withReadWriteToolAccess()
            .registerCustomProperty(" runtime ", " sdk ", " ios ")
            .withBundledHostConnection(bundledDeveloperConfigJson: "{developer}", bundledConfigJson: "{profile}")
            .withHostConnectionDiscoveryPort(45_200)
            .withHostConnectionProfileRetentionSeconds(120)
            .build()

        XCTAssertEqual(options.sampleFrequencyMilliseconds, 400)
        XCTAssertEqual(options.retentionPeriodSeconds, 120)
        XCTAssertFalse(options.enableFramesPerSecond)
        XCTAssertTrue(options.enableBatteryLevel)
        XCTAssertTrue(options.defaultMemoryChannels.contains(.managedHeap))
        XCTAssertFalse(options.defaultMemoryChannels.contains(.nativeHeap))
        XCTAssertEqual(options.sessionJpegCapture?.intervalMilliseconds, 2_000)
        XCTAssertEqual(options.touchCapture?.moveCaptureFramesPerSecond, 12)
        XCTAssertEqual(options.toolGuard, .readWrite)
        XCTAssertEqual(options.customProperties["runtime"]?["sdk"], "ios")
        XCTAssertEqual(options.hostConnection.bundledDeveloperConfigJson, "{developer}")
        XCTAssertEqual(options.hostConnection.bundledConfigJson, "{profile}")
        XCTAssertEqual(options.hostConnection.discoveryPort, 45_200)
        XCTAssertEqual(options.hostConnection.connectionProfileRetentionSeconds, 120)
    }

    func testBuilderCanStartFromExistingOptions() throws {
        let baseOptions = AnsightOptions(
            sampleFrequencyMilliseconds: 600,
            retentionPeriodSeconds: 180,
            toolGuard: .readOnly
        )

        let options = try AnsightOptions.createBuilder(baseOptions)
            .withAllToolAccess()
            .build()

        XCTAssertEqual(options.sampleFrequencyMilliseconds, 600)
        XCTAssertEqual(options.retentionPeriodSeconds, 180)
        XCTAssertEqual(options.toolGuard, .fullAccess)
    }
}
