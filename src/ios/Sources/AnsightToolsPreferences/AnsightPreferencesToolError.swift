import Foundation

internal enum AnsightPreferencesToolError: LocalizedError {
    case invalidArgument(String)
    case notAllowed(String)
    case platformUnsupported(String)

    var errorDescription: String? {
        switch self {
        case .invalidArgument(let message),
             .notAllowed(let message),
             .platformUnsupported(let message):
            return message
        }
    }
}
