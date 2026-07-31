import Foundation

protocol PairingSessionConnecting: Sendable {
    var localHostAddress: String? { get }

    func connect(
        document: ParsedPairingDocument,
        clientName: String,
        options: PairingConnectionOptions?
    ) async -> PairingConnectionAttempt
}

extension PairingSessionConnecting {
    var localHostAddress: String? { nil }
}
