import Foundation

public protocol HostConnectionConfigReading: Sendable {
    func canRead(_ kind: HostConnectionRequestKind) -> Bool
    func readConfigPayload(for request: HostConnectionRequest) async throws -> String?
}
