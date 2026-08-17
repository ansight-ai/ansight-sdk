import XCTest
@testable import AnsightLocation

final class AnsightLocationTests: XCTestCase {
    func testCaptureIsDisabledByDefault() async {
        let recorder = AnsightLocationRecorder()

        let result = await recorder.record(AnsightLocationSample(
            latitude: -33.8688,
            longitude: 151.2093
        ))

        XCTAssertFalse(result.success)
        XCTAssertEqual(result.message, "Observed location capture is disabled.")
    }

    func testEnabledOptionsClampPrivacyAndSamplingControls() {
        let options = AnsightLocationOptions.enabled(
            decimalPlaces: 20,
            minimumInterval: -1,
            minimumDistanceMeters: -.infinity
        )

        XCTAssertTrue(options.enabled)
        XCTAssertEqual(options.decimalPlaces, 7)
        XCTAssertEqual(options.minimumInterval, 0)
        XCTAssertEqual(options.minimumDistanceMeters, 0)
    }
}
