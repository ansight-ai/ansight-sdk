import Foundation
import XCTest
@testable import AnsightCore

final class PairingLiveSessionTransportTests: XCTestCase {
    func testScreenshotMetadataRequiresHostCapabilityAndNeverChangesBinaryPayload() async throws {
        for enabled in [false, true] {
            for capable in [false, true] {
                let socket = TestPairingWebSocket(sendBehavior: .complete)
                let transport = PairingLiveSessionTransport(webSocketFactory: { _ in socket })
                try await transport.attach(url: try XCTUnwrap(URL(string: "ws://127.0.0.1/test")))
                transport.supportsScreenshotGeometry = capable
                let geometry = ScreenshotCaptureGeometry(captureTarget: "appWindow", safeAreaInsets: nil,
                    systemUi: ["statusBar": .absent("Not captured"), "navigationBar": .absent("Not captured"), "displayCutout": .unknown("No bounds")])
                let frame = AnsightCapturedScreenFrame(capturedAtUtc: "2026-10-18T00:00:00Z", capturedAtEpochMilliseconds: 1000,
                    width: 10, height: 20, quality: 60, jpegData: Data("abc".utf8), geometry: enabled ? geometry : nil)
                let payload = Data([1, 2, 3, 4])
                let result = await transport.sendScreenFrame(frame, payload: payload)
                XCTAssertTrue(result.success)
                let messages = socket.messages()
                XCTAssertEqual(messages.count, enabled && capable ? 2 : 1)
                if case .data(let data) = messages.last { XCTAssertEqual(data, payload) }
                else { XCTFail("Expected unchanged binary payload") }
                if enabled && capable {
                    guard case .string(let text) = messages[0] else { return XCTFail("Metadata must precede pixels") }
                    let metadata = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any])
                    XCTAssertEqual(metadata["sha256"] as? String, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
                    XCTAssertEqual(metadata["capturedAtEpochMs"] as? Int, 1000)
                    XCTAssertEqual(metadata["width"] as? Int, 10)
                }
                await transport.close(notify: false)
                XCTAssertFalse(transport.supportsScreenshotGeometry)
            }
        }
    }

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

    func testClosingPreviousReceivePumpDoesNotCloseCurrentSocket() async throws {
        let firstSocket = TestPairingWebSocket(sendBehavior: .complete)
        let secondSocket = TestPairingWebSocket(sendBehavior: .complete)
        let factory = TestPairingWebSocketFactory(sockets: [firstSocket, secondSocket])
        let transport = PairingLiveSessionTransport(
            sendTimeoutSeconds: 0.2,
            webSocketFactory: factory.makeSocket
        )
        let url = try XCTUnwrap(URL(string: "ws://127.0.0.1/ansight-test"))

        try await transport.attach(url: url)
        try await transport.attach(url: url)
        try await Task.sleep(nanoseconds: 100_000_000)

        let result = await transport.sendText("still-open")
        await transport.close(notify: false)

        XCTAssertTrue(result.success)
        XCTAssertTrue(firstSocket.didCancel())
        XCTAssertTrue(secondSocket.didResume())
        XCTAssertEqual(secondSocket.sentMessageCount(), 1)
    }
}
