import Foundation

public enum AnsightSecureStorageToolError: LocalizedError, Equatable {
    case invalidArgument(String)
    case notAllowed(String)
    case operationFailed(String)
    case platformUnsupported(String)

    public var errorDescription: String? {
        switch self {
        case .invalidArgument(let message),
             .notAllowed(let message),
             .operationFailed(let message),
             .platformUnsupported(let message):
            return message
        }
    }
}
