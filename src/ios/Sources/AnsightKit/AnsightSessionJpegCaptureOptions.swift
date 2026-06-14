import Foundation

public struct AnsightSessionJpegCaptureOptions: Sendable, Codable, Equatable {
    public var intervalMilliseconds: Int
    public var quality: Int
    public var maxWidth: Int?

    public init(intervalMilliseconds: Int = 1_000, quality: Int = 80, maxWidth: Int? = nil) {
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
