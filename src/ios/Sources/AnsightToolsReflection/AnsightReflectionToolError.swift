import Foundation

public enum AnsightReflectionToolError: LocalizedError {
    case invalidArgument(String)
    case notAllowed(String)
    case unavailable(String)
    case unsupported(String)

    public var errorDescription: String? {
        switch self {
        case .invalidArgument(let message),
             .notAllowed(let message),
             .unavailable(let message),
             .unsupported(let message):
            return message
        }
    }
}
