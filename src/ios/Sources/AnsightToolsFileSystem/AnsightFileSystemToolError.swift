import Foundation

public enum AnsightFileSystemToolError: LocalizedError, Equatable {
    case invalidArgument(String)
    case notAllowed(String)
    case notFound(String)
    case unsupported(String)
    case operationFailed(String)

    public var errorDescription: String? {
        switch self {
        case .invalidArgument(let message),
             .notAllowed(let message),
             .notFound(let message),
             .unsupported(let message),
             .operationFailed(let message):
            return message
        }
    }
}
