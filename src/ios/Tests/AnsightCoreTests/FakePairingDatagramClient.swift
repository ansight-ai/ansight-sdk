import Foundation
@testable import AnsightCore

final class FakePairingDatagramClient: PairingDatagramClient, @unchecked Sendable {
    private let lock = NSLock()
    private let responseProvider: @Sendable (Data, String, Int) -> Data?
    private var requestCounter = 0
    private var hosts: [String] = []

    init(responseData: Data? = nil) {
        self.responseProvider = { _, _, _ in responseData }
    }

    init(responseProvider: @escaping @Sendable (String, Int) -> Data?) {
        self.responseProvider = { _, host, port in responseProvider(host, port) }
    }

    init(requestAwareResponseProvider: @escaping @Sendable (Data, String, Int) -> Data?) {
        responseProvider = requestAwareResponseProvider
    }

    var requestCount: Int {
        lock.withLock { requestCounter }
    }

    var requestedHosts: [String] {
        lock.withLock { hosts }
    }

    func sendConnectRequest(_ data: Data, host: String, port: Int, timeoutSeconds: TimeInterval) async throws -> Data? {
        lock.withLock {
            requestCounter += 1
            hosts.append(host)
        }
        return responseProvider(data, host, port)
    }
}
