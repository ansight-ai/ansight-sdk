import Foundation
@testable import AnsightKit

final class TestPairingWebSocket: PairingWebSocket, @unchecked Sendable {
    enum SendBehavior {
        case complete
        case hangUntilCancelled
    }

    private let lock = NSLock()
    private let sendBehavior: SendBehavior
    private var sentMessages: [URLSessionWebSocketTask.Message] = []
    private var resumeCount = 0
    private var cancelCount = 0

    init(sendBehavior: SendBehavior) {
        self.sendBehavior = sendBehavior
    }

    func resume() {
        lock.withLock {
            resumeCount += 1
        }
    }

    func cancel(with closeCode: URLSessionWebSocketTask.CloseCode, reason: Data?) {
        lock.withLock {
            cancelCount += 1
        }
    }

    func send(_ message: URLSessionWebSocketTask.Message) async throws {
        lock.withLock {
            sentMessages.append(message)
        }

        switch sendBehavior {
        case .complete:
            return
        case .hangUntilCancelled:
            while true {
                try await Task.sleep(nanoseconds: 10_000_000)
            }
        }
    }

    func receive() async throws -> URLSessionWebSocketTask.Message {
        while true {
            try await Task.sleep(nanoseconds: 10_000_000)
        }
    }

    func sentMessageCount() -> Int {
        lock.withLock { sentMessages.count }
    }

    func didResume() -> Bool {
        lock.withLock { resumeCount > 0 }
    }

    func didCancel() -> Bool {
        lock.withLock { cancelCount > 0 }
    }
}
