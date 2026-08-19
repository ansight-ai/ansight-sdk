import XCTest
@testable import AnsightCore

final class AnsightOptionsBuilderTests: XCTestCase {
    func testRuntimeDiagnosticChannelsAreReserved() {
        XCTAssertTrue(AnsightChannels.reservedIds.contains(AnsightChannels.jniReferenceCount))
        XCTAssertTrue(AnsightChannels.reservedIds.contains(AnsightChannels.openFileHandles))
        XCTAssertEqual(AnsightChannels.openFileHandlesChannel.name, "Open File Handles")
    }

    func testBuilderAppliesDotNetStyleOptionsConvention() throws {
        let options = try AnsightOptions.createBuilder()
            .withSampleFrequencyMilliseconds(400)
            .withRetentionPeriodSeconds(120)
            .withoutFramesPerSecond()
            .withBatteryLevel()
            .withOpenFileHandleTracking()
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
            .withUnattendedProvisioning()
            .build()

        XCTAssertEqual(options.sampleFrequencyMilliseconds, 400)
        XCTAssertEqual(options.retentionPeriodSeconds, 120)
        XCTAssertFalse(options.enableFramesPerSecond)
        XCTAssertTrue(options.enableBatteryLevel)
        XCTAssertTrue(options.enableOpenFileHandleTracking)
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
        XCTAssertTrue(options.hostConnection.allowUnattendedProvisioning)
        XCTAssertFalse(AnsightOptions().hostConnection.allowCellularConnections)
        XCTAssertFalse(AnsightOptions().hostConnection.allowUnattendedProvisioning)
    }

    func testOpenFileHandleTrackingDefaultsOffAndCanBeDisabledAgain() throws {
        XCTAssertFalse(AnsightOptions().enableOpenFileHandleTracking)

        let options = try AnsightOptions.createBuilder()
            .withOpenFileHandleTracking()
            .withoutOpenFileHandleTracking()
            .build()

        XCTAssertFalse(options.enableOpenFileHandleTracking)
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

    func testBuilderCanCaptureVisualTreesOnTouch() throws {
        let options = try AnsightOptions.createBuilder()
            .withSessionJpegCapture(mode: .screenshotWithVisualTreeOnTouch)
            .build()

        XCTAssertEqual(options.sessionJpegCapture?.mode, .screenshotWithVisualTreeOnTouch)
    }

    func testHostConnectionDecodeDefaultsCellularConnectionsToDisabled() throws {
        let json = #"{"savedConfigKey":"saved","connectionProfileRetentionSeconds":60}"#
        let data = try XCTUnwrap(json.data(using: .utf8))

        let options = try JSONDecoder().decode(AnsightHostConnectionOptions.self, from: data)

        XCTAssertFalse(options.allowCellularConnections)
        XCTAssertFalse(options.allowUnattendedProvisioning)
    }

    func testUnattendedProvisioningReadsOnlyAnEnabledNonBlankPayload() {
        let environment = [
            AnsightUnattendedProvisioning.payloadEnvironmentVariableName: "  ans2:test-payload  "
        ]

        XCTAssertNil(AnsightUnattendedProvisioning.payload(enabled: false, environment: environment))
        XCTAssertEqual(
            AnsightUnattendedProvisioning.payload(enabled: true, environment: environment),
            "ans2:test-payload"
        )
        XCTAssertNil(
            AnsightUnattendedProvisioning.payload(
                enabled: true,
                environment: [AnsightUnattendedProvisioning.payloadEnvironmentVariableName: "  "]
            )
        )
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

    func testCrashCaptureDefaultsOnAndClampsDurableOutboxBounds() throws {
        let options = try AnsightOptions.createBuilder()
            .withCrashCapture(
                AnsightCrashCaptureOptions(
                    maximumPendingReports: 100,
                    retentionDays: 0,
                    maximumBreadcrumbs: 1_000,
                    maximumTraceBytes: 1
                )
            )
            .build()

        XCTAssertTrue(options.crashCapture.enabled)
        XCTAssertEqual(options.crashCapture.maximumPendingReports, 32)
        XCTAssertEqual(options.crashCapture.retentionDays, 1)
        XCTAssertEqual(options.crashCapture.maximumBreadcrumbs, 256)
        XCTAssertEqual(options.crashCapture.maximumTraceBytes, 16 * 1_024)
        XCTAssertFalse(try AnsightOptions.createBuilder().withoutCrashCapture().build().crashCapture.enabled)
    }

    func testCrashCaptureDecodeDefaultsMissingFields() throws {
        let options = try JSONDecoder().decode(
            AnsightCrashCaptureOptions.self,
            from: Data(#"{}"#.utf8)
        )

        XCTAssertTrue(options.enabled)
        XCTAssertTrue(options.studioHandoffEnabled)
        XCTAssertTrue(options.offlineCaptureAttachmentEnabled)
        XCTAssertEqual(options.maximumPendingReports, 8)
        XCTAssertEqual(options.retentionDays, 7)
        XCTAssertEqual(options.maximumBreadcrumbs, 64)
        XCTAssertEqual(options.maximumTraceBytes, 1_048_576)
    }
}
