import Foundation

public struct AnsightLifecycleCaptureOptions: Sendable, Codable, Equatable {
    public static let enabledDefault = AnsightLifecycleCaptureOptions()
    public static let disabled = AnsightLifecycleCaptureOptions(enabled: false)

    public var enabled: Bool
    public var captureAppLifecycle: Bool
    public var captureScreenViews: Bool
    public var minimumScreenViewIntervalMilliseconds: Int

    public init(
        enabled: Bool = true,
        captureAppLifecycle: Bool = true,
        captureScreenViews: Bool = true,
        minimumScreenViewIntervalMilliseconds: Int = 250
    ) {
        self.enabled = enabled
        self.captureAppLifecycle = captureAppLifecycle
        self.captureScreenViews = captureScreenViews
        self.minimumScreenViewIntervalMilliseconds = minimumScreenViewIntervalMilliseconds
    }

    public mutating func validate() {
        minimumScreenViewIntervalMilliseconds = max(0, min(minimumScreenViewIntervalMilliseconds, 60_000))
    }
}

