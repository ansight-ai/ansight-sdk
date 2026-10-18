import Foundation

public struct AnnotationOptions: Sendable, Codable, Equatable {
    public var enabled: Bool
    public var captureScreenshot: Bool
    public var captureVisualTrees: Bool
    public var screenshotQuality: Int
    public var screenshotMaxWidth: Int
    public var includeSystemUiMetadata: Bool
    /// Set by a prebuilt bridge when its own compilation mode differs from the app's.
    public var debugBuildOverride: Bool?

    public init(
        enabled: Bool = true,
        captureScreenshot: Bool = true,
        captureVisualTrees: Bool = true,
        screenshotQuality: Int = 85,
        screenshotMaxWidth: Int = 1440,
        debugBuildOverride: Bool? = nil,
        includeSystemUiMetadata: Bool = true
    ) {
        self.enabled = enabled
        self.captureScreenshot = captureScreenshot
        self.captureVisualTrees = captureVisualTrees
        self.screenshotQuality = screenshotQuality
        self.screenshotMaxWidth = screenshotMaxWidth
        self.debugBuildOverride = debugBuildOverride
        self.includeSystemUiMetadata = includeSystemUiMetadata
    }

    private enum CodingKeys: String, CodingKey {
        case enabled, captureScreenshot, captureVisualTrees, screenshotQuality, screenshotMaxWidth, debugBuildOverride, includeSystemUiMetadata
    }

    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        self.init(enabled: try values.decodeIfPresent(Bool.self, forKey: .enabled) ?? true,
            captureScreenshot: try values.decodeIfPresent(Bool.self, forKey: .captureScreenshot) ?? true,
            captureVisualTrees: try values.decodeIfPresent(Bool.self, forKey: .captureVisualTrees) ?? true,
            screenshotQuality: try values.decodeIfPresent(Int.self, forKey: .screenshotQuality) ?? 85,
            screenshotMaxWidth: try values.decodeIfPresent(Int.self, forKey: .screenshotMaxWidth) ?? 1440,
            debugBuildOverride: try values.decodeIfPresent(Bool.self, forKey: .debugBuildOverride),
            includeSystemUiMetadata: try values.decodeIfPresent(Bool.self, forKey: .includeSystemUiMetadata) ?? true)
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
