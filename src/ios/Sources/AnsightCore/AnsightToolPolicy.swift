import Foundation

public enum AnsightToolPolicy: String, Sendable, Codable, CaseIterable, Comparable {
    case read
    case write
    case critical

    public static func < (lhs: AnsightToolPolicy, rhs: AnsightToolPolicy) -> Bool {
        lhs.rank < rhs.rank
    }

    private var rank: Int {
        switch self {
        case .read: 0
        case .write: 1
        case .critical: 2
        }
    }
}
