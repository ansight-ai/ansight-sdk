import Foundation
@testable import AnsightCore

final class TestPairingWebSocketFactory: @unchecked Sendable {
    private let lock = NSLock()
    private let sockets: [TestPairingWebSocket]
    private var nextIndex = 0

    init(sockets: [TestPairingWebSocket]) {
        self.sockets = sockets
    }

    func makeSocket(url: URL) -> any PairingWebSocket {
        lock.withLock {
            let socket = sockets[nextIndex]
            nextIndex += 1
            return socket
        }
    }
}
