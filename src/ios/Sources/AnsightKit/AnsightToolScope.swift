import Foundation

public enum AnsightToolScope: String, Sendable, Codable, CaseIterable {
    case read = "Read"
    case write = "Write"
    case delete = "Delete"
}
