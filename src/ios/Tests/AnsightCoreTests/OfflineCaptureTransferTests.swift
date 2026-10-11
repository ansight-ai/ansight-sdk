import Foundation
import XCTest
@testable import AnsightCore

final class OfflineCaptureTransferTests: XCTestCase {
    func testAutomaticTransferPurgesMatchingReceipt() async throws { try await exercise(mode: .automatic, badReceipt: false) }
    func testManualModeWaitsForExplicitTransfer() async throws { try await exercise(mode: .manual, badReceipt: false) }
    func testMismatchedReceiptRetainsCapture() async throws { try await exercise(mode: .automatic, badReceipt: true) }
    private func exercise(mode: OfflineCaptureTransferMode, badReceipt: Bool) async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let options = OfflineCaptureOptions(enabled: true, transferMode: mode)
        let store = try OfflineCaptureStore(root: directory, options: options, appId: "test", processSessionId: "process")
        let socket = OfflineReceiptSocket(badReceipt: badReceipt)
        let transport = PairingLiveSessionTransport()
        await transport.attach(authenticatedSocket: socket)
        let capture = NativeOfflineCapture(store: store, options: options, transport: transport)
        capture.update(active: true, connected: false)
        XCTAssertTrue(capture.append(.object(["metrics": .array([.object(["channel": .integer(1), "value": .integer(4)])])])))
        capture.update(active: true, connected: true)
        if mode == .manual {
            try await Task.sleep(nanoseconds: 50_000_000)
            XCTAssertEqual(socket.controlCount, 0)
            XCTAssertTrue(capture.transfer().success)
        }
        var completed = false
        for _ in 0..<300 {
            let sessions = try await store.sessions()
            if badReceipt ? capture.lastError != nil : sessions.isEmpty { completed = true; break }
            try await Task.sleep(nanoseconds: 10_000_000)
        }
        XCTAssertTrue(completed, capture.lastError ?? "Transfer timed out")
        let sessions = try await store.sessions()
        XCTAssertEqual(sessions.count, badReceipt ? 1 : 0)
        XCTAssertGreaterThan(socket.binaryBytes, 56)
        capture.update(active: false, connected: false)
        await transport.close()
    }
}

private final class OfflineReceiptSocket: PairingWebSocket, @unchecked Sendable {
    private let lock = NSLock()
    private let badReceipt: Bool
    private var messages: [URLSessionWebSocketTask.Message] = []
    private var waiter: CheckedContinuation<URLSessionWebSocketTask.Message, Error>?
    private var controls = 0
    private var bytes = 0
    private var closed = false
    init(badReceipt: Bool) { self.badReceipt = badReceipt }
    var controlCount: Int { lock.withLock { controls } }
    var binaryBytes: Int { lock.withLock { bytes } }
    func resume() {}
    func cancel(with closeCode: URLSessionWebSocketTask.CloseCode, reason: Data?) {
        let pending = lock.withLock { () -> CheckedContinuation<URLSessionWebSocketTask.Message, Error>? in
            closed = true
            let pending = waiter
            waiter = nil
            return pending
        }
        pending?.resume(throwing: CancellationError())
    }
    func send(_ message: URLSessionWebSocketTask.Message) async throws {
        guard case .string(let text) = message else {
            if case .data(let data) = message { lock.withLock { bytes += data.count } }
            return
        }
        let request = try JSONDecoder().decode(PairingControlEnvelope.self, from: Data(text.utf8))
        var payload: JSONValue?
        if request.action == "offline_capture.commit", case .object(var receipt) = request.payload {
            receipt["durable"] = .bool(true)
            if badReceipt { receipt["sha256"] = .string(String(repeating: "0", count: 64)) }
            payload = .object(receipt)
        }
        let response = PairingControlEnvelope(type: PairingControlEnvelope.responseType,
            replyTo: request.id, action: request.action, payload: payload, success: true)
        let outgoing = URLSessionWebSocketTask.Message.string(String(data: try JSONEncoder().encode(response), encoding: .utf8)!)
        let pending = lock.withLock { () -> CheckedContinuation<URLSessionWebSocketTask.Message, Error>? in
            controls += 1
            if let pending = waiter { waiter = nil; return pending }
            messages.append(outgoing)
            return nil
        }
        pending?.resume(returning: outgoing)
    }
    func receive() async throws -> URLSessionWebSocketTask.Message {
        try await withCheckedThrowingContinuation { continuation in
            lock.withLock {
                if closed { continuation.resume(throwing: CancellationError()) }
                else if !messages.isEmpty { continuation.resume(returning: messages.removeFirst()) }
                else { waiter = continuation }
            }
        }
    }
}
