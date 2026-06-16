import Foundation
@testable import AnsightCore

final class FakePairingSessionConnector: PairingSessionConnecting, @unchecked Sendable {
    private let lock = NSLock()
    private let attemptsByConfigId: [String: PairingConnectionAttempt]
    private let responseDelayNanoseconds: UInt64
    private var configIds: [String] = []

    init(
        attemptsByConfigId: [String: PairingConnectionAttempt],
        responseDelayNanoseconds: UInt64 = 0
    ) {
        self.attemptsByConfigId = attemptsByConfigId
        self.responseDelayNanoseconds = responseDelayNanoseconds
    }

    var attemptedConfigIds: [String] {
        lock.withLock { configIds }
    }

    func connect(
        document: ParsedPairingDocument,
        clientName: String,
        options: PairingConnectionOptions?
    ) async -> PairingConnectionAttempt {
        lock.withLock {
            configIds.append(document.config.configId)
        }

        if responseDelayNanoseconds > 0 {
            try? await Task.sleep(nanoseconds: responseDelayNanoseconds)
        }

        return attemptsByConfigId[document.config.configId] ?? .failure(
            "No fake pairing connection attempt was registered for \(document.config.configId).",
            code: PairingFailureCodes.udpBootstrapFailed
        )
    }
}
