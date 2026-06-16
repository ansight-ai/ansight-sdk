import Foundation

protocol PairingSessionConnecting: Sendable {
    func connect(
        document: ParsedPairingDocument,
        clientName: String,
        options: PairingConnectionOptions?
    ) async -> PairingConnectionAttempt
}
