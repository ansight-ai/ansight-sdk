import Foundation

enum BuildToolError: LocalizedError {
    case invalidArguments(String)
    case writeFailed(String)

    var errorDescription: String? {
        switch self {
        case .invalidArguments(let message), .writeFailed(let message):
            return message
        }
    }
}
