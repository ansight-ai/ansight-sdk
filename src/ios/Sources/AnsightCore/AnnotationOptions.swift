import Foundation

public struct AnnotationOptions: Sendable, Codable, Equatable {
    public var enabled: Bool
    public var captureScreenshot: Bool
    public var captureVisualTrees: Bool
    public var screenshotQuality: Int
    public var screenshotMaxWidth: Int
    /// Set by a prebuilt bridge when its own compilation mode differs from the app's.
    public var debugBuildOverride: Bool?

    public init(
        enabled: Bool = true,
        captureScreenshot: Bool = true,
        captureVisualTrees: Bool = true,
        screenshotQuality: Int = 85,
        screenshotMaxWidth: Int = 1440,
        debugBuildOverride: Bool? = nil
    ) {
        self.enabled = enabled
        self.captureScreenshot = captureScreenshot
        self.captureVisualTrees = captureVisualTrees
        self.screenshotQuality = screenshotQuality
        self.screenshotMaxWidth = screenshotMaxWidth
        self.debugBuildOverride = debugBuildOverride
    }

    public func validated() -> AnnotationOptions {
        var copy = self
        copy.screenshotQuality = min(max(screenshotQuality, 1), 100)
        copy.screenshotMaxWidth = min(max(screenshotMaxWidth, 1), 8192)
        return copy
    }
}

public enum AnnotationCaptureStatus: String, Sendable, Codable {
    case completed, queued, cancelled, disabled, unavailable, failed
}

public struct AnnotationCaptureResult: Sendable {
    public let status: AnnotationCaptureStatus
    public let annotationId: UUID?
    public let message: String?

    public init(status: AnnotationCaptureStatus, annotationId: UUID? = nil, message: String? = nil) {
        self.status = status
        self.annotationId = annotationId
        self.message = message
    }

    public var isSuccess: Bool { status == .completed || status == .queued }
}
