import Foundation

public struct AnsightSessionJpegCaptureOptions: Sendable, Codable, Equatable {
    public static let defaultIntervalMilliseconds = 2_000
    public static let defaultQuality = 60
    public static let defaultMaxWidth = 480

    public var intervalMilliseconds: Int
    public var quality: Int
    public var maxWidth: Int?

    public init(
        intervalMilliseconds: Int = Self.defaultIntervalMilliseconds,
        quality: Int = Self.defaultQuality,
        maxWidth: Int? = Self.defaultMaxWidth
    ) {
        self.intervalMilliseconds = intervalMilliseconds
        self.quality = quality
        self.maxWidth = maxWidth
    }

    public mutating func validate() {
        intervalMilliseconds = max(250, intervalMilliseconds)
        quality = max(1, min(quality, 100))
        if let maxWidth {
            self.maxWidth = maxWidth <= 0 ? nil : min(maxWidth, 8_192)
        }
    }
}
