import Foundation

public enum AnsightSessionJpegCaptureMode: String, Sendable, Codable, Equatable {
    case screenshotOnly
    case screenshotAndVisualTree
    case screenshotWithVisualTreeOnTouch
}

public struct AnsightSessionJpegCaptureOptions: Sendable, Codable, Equatable {
    public static let defaultIntervalMilliseconds = 2_000
    public static let defaultQuality = 60
    public static let defaultMaxWidth = 480
    public static let defaultCaptureGpuBackedSurfaces = true
    public static let defaultCaptureKeyboardPresence = false
    public static let defaultMode = AnsightSessionJpegCaptureMode.screenshotOnly

    public var intervalMilliseconds: Int
    public var quality: Int
    public var maxWidth: Int?
    public var captureGpuBackedSurfaces: Bool
    public var captureKeyboardPresence: Bool
    public var mode: AnsightSessionJpegCaptureMode

    public init(
        intervalMilliseconds: Int = Self.defaultIntervalMilliseconds,
        quality: Int = Self.defaultQuality,
        maxWidth: Int? = Self.defaultMaxWidth,
        captureGpuBackedSurfaces: Bool = Self.defaultCaptureGpuBackedSurfaces,
        mode: AnsightSessionJpegCaptureMode = Self.defaultMode,
        captureKeyboardPresence: Bool = Self.defaultCaptureKeyboardPresence
    ) {
        self.intervalMilliseconds = intervalMilliseconds
        self.quality = quality
        self.maxWidth = maxWidth
        self.captureGpuBackedSurfaces = captureGpuBackedSurfaces
        self.captureKeyboardPresence = captureKeyboardPresence
        self.mode = mode
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        intervalMilliseconds = try container.decodeIfPresent(Int.self, forKey: .intervalMilliseconds)
            ?? Self.defaultIntervalMilliseconds
        quality = try container.decodeIfPresent(Int.self, forKey: .quality)
            ?? Self.defaultQuality
        maxWidth = container.contains(.maxWidth)
            ? try container.decodeIfPresent(Int.self, forKey: .maxWidth)
            : Self.defaultMaxWidth
        captureGpuBackedSurfaces = try container.decodeIfPresent(Bool.self, forKey: .captureGpuBackedSurfaces)
            ?? Self.defaultCaptureGpuBackedSurfaces
        captureKeyboardPresence = try container.decodeIfPresent(Bool.self, forKey: .captureKeyboardPresence)
            ?? Self.defaultCaptureKeyboardPresence
        mode = try container.decodeIfPresent(AnsightSessionJpegCaptureMode.self, forKey: .mode)
            ?? Self.defaultMode
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(intervalMilliseconds, forKey: .intervalMilliseconds)
        try container.encode(quality, forKey: .quality)
        try container.encodeIfPresent(maxWidth, forKey: .maxWidth)
        try container.encode(captureGpuBackedSurfaces, forKey: .captureGpuBackedSurfaces)
        try container.encode(captureKeyboardPresence, forKey: .captureKeyboardPresence)
        try container.encode(mode, forKey: .mode)
    }

    public mutating func validate() {
        intervalMilliseconds = max(250, intervalMilliseconds)
        quality = max(1, min(quality, 100))
        if let maxWidth {
            self.maxWidth = maxWidth <= 0 ? nil : min(maxWidth, 8_192)
        }
    }

    private enum CodingKeys: String, CodingKey {
        case intervalMilliseconds
        case quality
        case maxWidth
        case captureGpuBackedSurfaces
        case captureKeyboardPresence
        case mode
    }
}
