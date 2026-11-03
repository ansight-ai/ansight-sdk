import Foundation

public enum OfflineCaptureError: LocalizedError {
    case archiveTooLarge
    case invalidOptions
    case invalidSession
    case sessionBusy
    case invalidReceipt
    case retentionBlocked
    case persistenceFailed(String)

    public var errorDescription: String? {
        switch self {
        case .archiveTooLarge: return "The offline capture exceeds its archive budget."
        case .invalidOptions: return "The offline capture configuration is invalid."
        case .invalidSession: return "The offline capture is unavailable or invalid."
        case .sessionBusy: return "An offline capture upload is already in progress."
        case .invalidReceipt: return "The upload service returned an invalid capture receipt."
        case .retentionBlocked: return "Offline capture is paused until retained chunks are uploaded."
        case .persistenceFailed(let message): return message
        }
    }
}
