import Foundation

public enum AppLifecycleState: String, Sendable, Codable, CaseIterable {
    case unknown
    case foreground
    case background
}
