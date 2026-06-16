import Foundation
@testable import AnsightCore

final class FakePairingDatagramClient: PairingDatagramClient, @unchecked Sendable {
    private let lock = NSLock()
    private let responseData: Data?
    private var requestCounter = 0

    init(responseData: Data? = nil) {
        self.responseData = responseData
    }

    var requestCount: Int {
        lock.withLock { requestCounter }
    }

    func sendConnectRequest(_ data: Data, host: String, port: Int, timeoutSeconds: TimeInterval) async throws -> Data? {
        lock.withLock {
            requestCounter += 1
        }
        return responseData
    }
}
