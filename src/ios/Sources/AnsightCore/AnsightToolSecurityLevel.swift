import Foundation

public enum AnsightToolSecurityLevel: String, Sendable, Codable, CaseIterable {
    case unspecified = "Unspecified"
    case low = "Low"
    case moderate = "Moderate"
    case high = "High"
    case critical = "Critical"
}
