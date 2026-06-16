import Foundation

public enum HostConnectionRequestKind: String, Sendable, Codable, CaseIterable {
    case auto
    case savedConfig
    case bundledConfig
    case file
    case qrCode
    case payload
    case config
}
