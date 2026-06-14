import Foundation

internal enum AnsightDatabaseToolError: LocalizedError {
    case invalidArgument(String)
    case notAllowed(String)
    case notFound(String)
    case operationFailed(String)

    var errorDescription: String? {
        switch self {
        case .invalidArgument(let message),
             .notAllowed(let message),
             .notFound(let message),
             .operationFailed(let message):
            return message
        }
    }
}
