import Foundation
@testable import AnsightCore

final class FakePairingDatagramClient: PairingDatagramClient, @unchecked Sendable {
    private let lock = NSLock()
    private let responseProvider: @Sendable (String, Int) -> Data?
    private var requestCounter = 0
    private var hosts: [String] = []

    init(responseData: Data? = nil) {
        self.responseProvider = { _, _ in responseData }
    }

    init(responseProvider: @escaping @Sendable (String, Int) -> Data?) {
        self.responseProvider = responseProvider
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
        let response = responseProvider(host, port)
        guard let response,
              var responseObject = try? JSONSerialization.jsonObject(with: response) as? [String: Any],
              let requestObject = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let requestId = requestObject["requestId"] as? String
        else {
            return response
        }

        responseObject["requestId"] = requestId
        return try JSONSerialization.data(withJSONObject: responseObject)
    }
}
