import Foundation

public enum AnsightEventType: String, Sendable, Codable, CaseIterable {
    case event
    case debug
    case info
    case warning
    case error
    case exception
    case gc
    case navigation
    case screenViewed
    case lifecycle

    var wireName: String {
        switch self {
        case .event:
            return "Event"
        case .debug:
            return "Debug"
        case .info:
            return "Info"
        case .warning:
            return "Warning"
        case .error:
            return "Error"
        case .exception:
            return "Exception"
        case .gc:
            return "Gc"
        case .navigation:
            return "Navigation"
        case .screenViewed:
            return "ScreenViewed"
        case .lifecycle:
            return "Lifecycle"
        }
    }
}
