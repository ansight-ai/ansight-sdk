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
            .withBundledHostConnection(bundledConfigJson: "{profile}")
            .withHostConnectionDiscoveryPort(45_200)
            .withHostConnectionProfileRetentionSeconds(120)
            .withCellularHostConnections()
            .build()

        XCTAssertEqual(options.sampleFrequencyMilliseconds, 400)
        XCTAssertEqual(options.retentionPeriodSeconds, 120)
        XCTAssertFalse(options.enableFramesPerSecond)
        XCTAssertTrue(options.enableBatteryLevel)
        XCTAssertTrue(options.defaultMemoryChannels.contains(.managedHeap))
        XCTAssertFalse(options.defaultMemoryChannels.contains(.nativeHeap))
        XCTAssertEqual(options.sessionJpegCapture?.intervalMilliseconds, 2_000)
        XCTAssertEqual(options.sessionJpegCapture?.quality, 60)
        XCTAssertEqual(options.sessionJpegCapture?.maxWidth, 480)
        XCTAssertEqual(options.sessionJpegCapture?.captureGpuBackedSurfaces, true)
        XCTAssertEqual(options.sessionJpegCapture?.mode, .screenshotOnly)
        XCTAssertEqual(options.touchCapture?.moveCaptureFramesPerSecond, 12)
        XCTAssertEqual(options.toolGuard, .readWrite)
        XCTAssertEqual(options.customProperties["runtime"]?["sdk"], "ios")
        XCTAssertEqual(options.hostConnection.bundledConfigJson, "{profile}")
        XCTAssertEqual(options.hostConnection.discoveryPort, 45_200)
        XCTAssertEqual(options.hostConnection.connectionProfileRetentionSeconds, 120)
        XCTAssertTrue(options.hostConnection.allowCellularConnections)
        XCTAssertFalse(AnsightOptions().hostConnection.allowCellularConnections)
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

    func testBuilderCanDisableGpuBackedSurfaceCapture() throws {
        let options = try AnsightOptions.createBuilder()
            .withSessionJpegCapture(captureGpuBackedSurfaces: false)
            .build()

        XCTAssertEqual(options.sessionJpegCapture?.captureGpuBackedSurfaces, false)
    }

    func testBuilderCanCaptureScreenshotAndVisualTree() throws {
        let options = try AnsightOptions.createBuilder()
            .withSessionJpegCapture(mode: .screenshotAndVisualTree)
            .build()

        XCTAssertEqual(options.sessionJpegCapture?.mode, .screenshotAndVisualTree)
    }

    func testHostConnectionDecodeDefaultsCellularConnectionsToDisabled() throws {
        let json = #"{"savedConfigKey":"saved","connectionProfileRetentionSeconds":60}"#
        let data = try XCTUnwrap(json.data(using: .utf8))

        let options = try JSONDecoder().decode(AnsightHostConnectionOptions.self, from: data)

        XCTAssertFalse(options.allowCellularConnections)
    }

    func testSessionJpegCaptureDecodeDefaultsGpuBackedSurfaceCapture() throws {
        let json = #"{"intervalMilliseconds":1000,"quality":70,"maxWidth":null}"#
        let data = try XCTUnwrap(json.data(using: .utf8))

        let options = try JSONDecoder().decode(AnsightSessionJpegCaptureOptions.self, from: data)

        XCTAssertNil(options.maxWidth)
        XCTAssertEqual(options.captureGpuBackedSurfaces, true)
        XCTAssertEqual(options.mode, .screenshotOnly)
    }

    func testSessionJpegCaptureDecodePreservesGpuBackedSurfaceCapture() throws {
        let json = #"{"captureGpuBackedSurfaces":false}"#
        let data = try XCTUnwrap(json.data(using: .utf8))

        let options = try JSONDecoder().decode(AnsightSessionJpegCaptureOptions.self, from: data)

        XCTAssertEqual(options.intervalMilliseconds, 2_000)
        XCTAssertEqual(options.quality, 60)
        XCTAssertEqual(options.maxWidth, 480)
        XCTAssertEqual(options.captureGpuBackedSurfaces, false)
    }
}
