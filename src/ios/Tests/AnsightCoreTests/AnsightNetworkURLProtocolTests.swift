import Foundation
import XCTest
@testable import AnsightCore

final class AnsightNetworkURLProtocolTests: XCTestCase {
    func testWebSocketAndUpgradeChannelsAreNeverCaptured() throws {
        AnsightNativeNetworkCapture.configure(AnsightNetworkCaptureOptions(enabled: true))
        defer { AnsightNativeNetworkCapture.configure(AnsightNetworkCaptureOptions(enabled: false)) }

        let webSocketRequest = URLRequest(
            url: try XCTUnwrap(URL(string: "wss://127.0.0.1/ws"))
        )
        var upgradeRequest = URLRequest(
            url: try XCTUnwrap(URL(string: "https://127.0.0.1/ws"))
        )
        upgradeRequest.setValue("websocket", forHTTPHeaderField: "Upgrade")

        XCTAssertFalse(AnsightNetworkURLProtocol.canInit(with: webSocketRequest))
        XCTAssertFalse(AnsightNetworkURLProtocol.canInit(with: upgradeRequest))
    }

    func testAnsightInternalHttpTrafficIsHandledOnlyToRemoveItsMarker() throws {
        AnsightNativeNetworkCapture.configure(AnsightNetworkCaptureOptions(enabled: true))
        defer { AnsightNativeNetworkCapture.configure(AnsightNetworkCaptureOptions(enabled: false)) }

        var request = URLRequest(
            url: try XCTUnwrap(URL(string: "https://example.test/upload"))
        )
        request.setValue("1", forHTTPHeaderField: "X-Ansight-Internal-Traffic")

        XCTAssertTrue(AnsightNetworkURLProtocol.canInit(with: request))
    }
}
