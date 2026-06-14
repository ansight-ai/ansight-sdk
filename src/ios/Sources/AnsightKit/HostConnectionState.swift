import Foundation

public enum HostConnectionState: String, Sendable, Codable, CaseIterable {
    case disconnected
    case connecting
    case connected
}
