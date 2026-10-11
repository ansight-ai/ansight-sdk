public enum OfflineCaptureError: Error {
    case archiveTooLarge
    case invalidOptions
    case invalidSession
    case sessionBusy
    case invalidReceipt
    case retentionBlocked
    case persistenceFailed(String)
}
