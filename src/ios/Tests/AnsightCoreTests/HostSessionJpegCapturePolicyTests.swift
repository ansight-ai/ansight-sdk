import XCTest
@testable import AnsightCore

final class HostSessionJpegCapturePolicyTests: XCTestCase {
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
