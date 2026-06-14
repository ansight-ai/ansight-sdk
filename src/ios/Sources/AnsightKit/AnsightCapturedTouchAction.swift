import Foundation

enum AnsightCapturedTouchAction: Sendable, Codable, Equatable {
    case down
    case move
    case up
    case cancel
    case unknown

    var wireCode: Int {
        switch self {
        case .down:
            return 0
        case .move:
            return 1
        case .up:
            return 2
        case .cancel:
            return 3
        case .unknown:
            return 4
        }
    }
}
