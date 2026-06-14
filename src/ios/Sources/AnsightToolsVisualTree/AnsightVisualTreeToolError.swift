import Foundation

internal enum AnsightVisualTreeToolError: LocalizedError {
    case invalidArgument(String)
    case platformUnsupported
    case unavailable(String)

    var errorDescription: String? {
        switch self {
        case .invalidArgument(let message):
            message
        case .platformUnsupported:
            "Visual tree tools are only supported on iOS and Mac Catalyst."
        case .unavailable(let message):
            message
        }
    }

    var errorCode: String {
        switch self {
        case .invalidArgument:
            "visual_tree_invalid_argument"
        case .platformUnsupported:
            "visual_tree_platform_unsupported"
        case .unavailable:
            "visual_tree_unavailable"
        }
    }
}
