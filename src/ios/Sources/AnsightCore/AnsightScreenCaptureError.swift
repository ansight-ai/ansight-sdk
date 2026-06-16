import Foundation

public enum AnsightScreenCaptureError: LocalizedError, Sendable, Equatable {
    case unavailable
    case noWindow
    case encodingFailed

    public var errorDescription: String? {
        switch self {
        case .unavailable:
            return "Screen capture is only available on UIKit platforms."
        case .noWindow:
            return "No foreground window is available for screen capture."
        case .encodingFailed:
            return "Captured screen image could not be encoded as JPEG."
        }
    }
}
