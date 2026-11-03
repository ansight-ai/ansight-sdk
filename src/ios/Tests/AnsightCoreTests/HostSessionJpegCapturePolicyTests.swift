import XCTest
@testable import AnsightCore

final class HostSessionJpegCapturePolicyTests: XCTestCase {
    func testPeriodicPauseAcknowledgesAndResumesWithoutChangingCaptureOwner() throws {
        let runtime = AnsightRuntime.shared
        defer { runtime.clear() }
        for paused in [true, false] {
            let request = AnsightToolProtocolEnvelope(type: "session.screenshots", id: "capture-policy",
                payload: .object(["paused": .bool(paused)]))
            let response = try XCTUnwrap(runtime.handleToolProtocolMessage(try JSONValue.fromEncodable(request).jsonString()))
            let envelope = try JSONDecoder().decode(AnsightToolProtocolEnvelope.self, from: Data(response.utf8))
            XCTAssertEqual(envelope.type, "session.screenshots.result")
            XCTAssertEqual(envelope.replyTo, "capture-policy")
            XCTAssertEqual(runtime.isPeriodicScreenshotCapturePaused, paused)
        }
    }

    func testClearingSessionResetsPeriodicPause() throws {
        let runtime = AnsightRuntime.shared
        let request = AnsightToolProtocolEnvelope(type: "session.screenshots", id: "capture-policy",
            payload: .object(["paused": .bool(true)]))
        _ = try runtime.handleToolProtocolMessage(try JSONValue.fromEncodable(request).jsonString())
        runtime.clear()
        XCTAssertFalse(runtime.isPeriodicScreenshotCapturePaused)
    }

    func testHostModeDisablesSdkCapture() {
        let policy = HostSessionJpegCapturePolicy(
            payload: .object([
                "sessionJpegCapture": .object([
                    "mode": .string("host"),
                    "source": .string("simctl"),
                ]),
            ])
        )

        XCTAssertTrue(policy.useHostCapture)
        XCTAssertEqual(policy.source, "simctl")
        XCTAssertEqual(HostSessionJpegCapturePolicy.controlVersion, 1)
        XCTAssertEqual(
            HostSessionJpegCapturePolicy.controlVersionPropertyName,
            "sessionJpegCaptureControlVersion"
        )
    }

    func testMissingOrAppModeKeepsSdkCapture() {
        XCTAssertFalse(HostSessionJpegCapturePolicy(payload: nil).useHostCapture)
        XCTAssertFalse(
            HostSessionJpegCapturePolicy(
                payload: .object([
                    "sessionJpegCapture": .object([
                        "mode": .string("app"),
                    ]),
                ])
            ).useHostCapture
        )
    }
}
