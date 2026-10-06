import Foundation

/// App-fed motion evidence. Ansight never starts a Core Motion sampler.
public struct AnsightMotionCaptureOptions: Sendable, Codable, Equatable {
    public var captureShake: Bool
    public var captureAccelerometer: Bool
    public var minimumSampleIntervalMilliseconds: Int

    public init(
        captureShake: Bool = true,
        captureAccelerometer: Bool = true,
        minimumSampleIntervalMilliseconds: Int = 20
    ) {
        self.captureShake = captureShake
        self.captureAccelerometer = captureAccelerometer
        self.minimumSampleIntervalMilliseconds = minimumSampleIntervalMilliseconds
    }

    public func validated() -> AnsightMotionCaptureOptions {
        var copy = self
        copy.minimumSampleIntervalMilliseconds = min(max(copy.minimumSampleIntervalMilliseconds, 10), 1_000)
        return copy
    }
}
