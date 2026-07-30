import Foundation

public struct AnsightDebugSnapshot: Sendable, Codable {
    public let initialized: Bool
    public let active: Bool
    public let sessionOpen: Bool
    public let metricsRecorded: Int
    public let eventsRecorded: Int
    public let registeredTools: Int
    public let executableTools: Int
    public let toolDiscoveryEnabled: Bool
    public let toolExecutionEnabled: Bool
    public let detectedBundledTools: [String]
    public let lastMetric: RecordedMetric?
    public let lastEvent: RecordedEvent?
    public let lastPairingConfigId: String?
    public let resolvedHostAddress: String?
    public let sessionMessage: String?
    public let lifecycleState: AppLifecycleState
    public let currentScreen: RecordedScreenView?
    public let channels: [AnsightChannel]
    public let hostConnectionStatus: HostConnectionStatus
    public let screenCaptureActive: Bool
    public let screenFramesCaptured: Int
    public let screenFramesSent: Int
    public let lastScreenCaptureMessage: String?
    public let lastScreenCaptureRenderMilliseconds: Int?
    public let lastScreenCaptureEncodeMilliseconds: Int?
    public let lastScreenCaptureSendMilliseconds: Int?
    public let lastScreenCaptureTotalMilliseconds: Int?
    public let frameRateCaptureActive: Bool
    public let lastFrameRate: Int?
    public let touchCaptureEnabled: Bool
    public let touchCaptureActive: Bool
    public let touchCaptureStreamingActive: Bool
    public let touchesCaptured: Int
    public let touchesSent: Int
    public let lastTouchCaptureMessage: String?
}
