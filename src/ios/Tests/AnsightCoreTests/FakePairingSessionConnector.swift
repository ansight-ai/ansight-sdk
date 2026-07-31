import Foundation
@testable import AnsightCore

final class FakePairingSessionConnector: PairingSessionConnecting, @unchecked Sendable {
    private let lock = NSLock()
    private let attemptsByConfigId: [String: PairingConnectionAttempt]
    private let responseDelayNanoseconds: UInt64
    let localHostAddress: String?
    private var configIds: [String] = []
    private var discoveryPorts: [Int] = []

    init(
        attemptsByConfigId: [String: PairingConnectionAttempt],
        responseDelayNanoseconds: UInt64 = 0,
        localHostAddress: String? = nil
    ) {
        self.attemptsByConfigId = attemptsByConfigId
        self.responseDelayNanoseconds = responseDelayNanoseconds
        self.localHostAddress = localHostAddress
    }

    var attemptedConfigIds: [String] {
        lock.withLock { configIds }
    }

    var attemptedDiscoveryPorts: [Int] {
        lock.withLock { discoveryPorts }
    }

    func connect(
        document: ParsedPairingDocument,
        clientName: String,
        options: PairingConnectionOptions?
    ) async -> PairingConnectionAttempt {
        lock.withLock {
            configIds.append(document.config.configId)
            discoveryPorts.append(
                options?.discoveryPort
                    ?? document.discoveryHint?.discoveryPort
                    ?? document.config.host.discoveryPort
            )
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
