import Foundation
import Network

protocol PairingDatagramClient: Sendable {
    func sendConnectRequest(_ data: Data, host: String, port: Int, timeoutSeconds: TimeInterval) async throws -> Data?
}
