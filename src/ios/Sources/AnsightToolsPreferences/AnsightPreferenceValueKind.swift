import Foundation

public enum AnsightPreferenceValueKind: String, Sendable, Codable, CaseIterable {
    case string
    case boolean
    case integer
    case number
    case stringArray = "string_array"
    case unsupported
}
