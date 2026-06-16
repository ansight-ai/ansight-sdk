import Foundation

public struct AnsightTouchCaptureOptions: Sendable, Codable, Equatable {
    public static let defaultMoveCaptureDistanceThreshold = 4.0
    public static let defaultMoveCaptureFramesPerSecond = 15

    public var captureMoveEvents: Bool
    public var captureCancelEvents: Bool
    public var moveCaptureDistanceThreshold: Double
    public var moveCaptureFramesPerSecond: Int

    public init(
        captureMoveEvents: Bool = true,
        captureCancelEvents: Bool = true,
        moveCaptureDistanceThreshold: Double = AnsightTouchCaptureOptions.defaultMoveCaptureDistanceThreshold,
        moveCaptureFramesPerSecond: Int = AnsightTouchCaptureOptions.defaultMoveCaptureFramesPerSecond
    ) {
        self.captureMoveEvents = captureMoveEvents
        self.captureCancelEvents = captureCancelEvents
        self.moveCaptureDistanceThreshold = moveCaptureDistanceThreshold
        self.moveCaptureFramesPerSecond = moveCaptureFramesPerSecond
    }

    public mutating func validate() {
        if !moveCaptureDistanceThreshold.isFinite || moveCaptureDistanceThreshold < 0 {
            moveCaptureDistanceThreshold = Self.defaultMoveCaptureDistanceThreshold
        }

        if moveCaptureFramesPerSecond < 0 {
            moveCaptureFramesPerSecond = Self.defaultMoveCaptureFramesPerSecond
        } else {
            moveCaptureFramesPerSecond = min(moveCaptureFramesPerSecond, 120)
        }
    }
}
