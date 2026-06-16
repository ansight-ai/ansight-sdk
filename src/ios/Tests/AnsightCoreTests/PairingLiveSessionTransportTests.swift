import Foundation
import XCTest
@testable import AnsightCore

final class PairingLiveSessionTransportTests: XCTestCase {
    func testSendDataTimesOutAndClosesTransportWhenHostSendStalls() async throws {
        let socket = TestPairingWebSocket(sendBehavior: .hangUntilCancelled)
        let transport = PairingLiveSessionTransport(
            sendTimeoutSeconds: 0.2,
            webSocketFactory: { _ in socket }
        )
        try await transport.attach(url: try XCTUnwrap(URL(string: "ws://127.0.0.1/ansight-test")))

        let startedAt = Date()
        let result = await transport.sendData(Data([0x01, 0x02, 0x03]))

        XCTAssertFalse(result.success)
        XCTAssertTrue(result.message.contains("Timed out sending WebSocket payload."))
        XCTAssertFalse(transport.isOpen)
        XCTAssertTrue(socket.didResume())
        XCTAssertTrue(socket.didCancel())
        XCTAssertLessThan(Date().timeIntervalSince(startedAt), 2)
    }

    func testControlRequestTimesOutWhenHostDoesNotAcknowledge() async throws {
        let socket = TestPairingWebSocket(sendBehavior: .complete)
        let transport = PairingLiveSessionTransport(
            sendTimeoutSeconds: 0.2,
            webSocketFactory: { _ in socket }
        )
        try await transport.attach(url: try XCTUnwrap(URL(string: "ws://127.0.0.1/ansight-test")))

        let result = await transport.sendControlRequest(
            action: "TEST_ACTION",
            payload: nil,
            acknowledgementTimeoutSeconds: 0.2
        )
        await transport.close(notify: false)

        XCTAssertFalse(result.success)
        XCTAssertTrue(result.message.contains("Timed out waiting for host acknowledgement."))
        XCTAssertEqual(socket.sentMessageCount(), 1)
        XCTAssertTrue(socket.didResume())
        XCTAssertTrue(socket.didCancel())
    }
}
