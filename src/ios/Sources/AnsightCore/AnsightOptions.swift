import Foundation

public struct AnsightOptions: Sendable, Codable, Equatable {
    public var sampleFrequencyMilliseconds: Int
    public var retentionPeriodSeconds: Int
    public var additionalChannels: [AnsightChannel]
    public var defaultMemoryChannels: DefaultMemoryChannels
    public var enableFramesPerSecond: Bool
    public var enableBatteryLevel: Bool
    public var lifecycleCapture: AnsightLifecycleCaptureOptions
    public var sessionJpegCapture: AnsightSessionJpegCaptureOptions?
    public var touchCapture: AnsightTouchCaptureOptions?
    public var toolGuard: AnsightToolGuard
    public var customProperties: [String: [String: String]]
    public var hostAutoProbe: AnsightHostAutoProbeOptions
    public var hostConnection: AnsightHostConnectionOptions

    public init(
        sampleFrequencyMilliseconds: Int = AnsightSamplingLimits.defaultSampleFrequencyMilliseconds,
        retentionPeriodSeconds: Int = AnsightSamplingLimits.defaultRetentionPeriodSeconds,
        additionalChannels: [AnsightChannel] = [],
        defaultMemoryChannels: DefaultMemoryChannels = .platformDefaults,
        enableFramesPerSecond: Bool = true,
        enableBatteryLevel: Bool = false,
        lifecycleCapture: AnsightLifecycleCaptureOptions = .enabledDefault,
        sessionJpegCapture: AnsightSessionJpegCaptureOptions? = nil,
        touchCapture: AnsightTouchCaptureOptions? = AnsightTouchCaptureOptions(),
        toolGuard: AnsightToolGuard = .disabled,
        customProperties: [String: [String: String]] = [:],
        hostAutoProbe: AnsightHostAutoProbeOptions = .enabledDefault,
        hostConnection: AnsightHostConnectionOptions = AnsightHostConnectionOptions()
    ) {
        self.sampleFrequencyMilliseconds = sampleFrequencyMilliseconds
        self.retentionPeriodSeconds = retentionPeriodSeconds
        self.additionalChannels = additionalChannels
        self.defaultMemoryChannels = defaultMemoryChannels
        self.enableFramesPerSecond = enableFramesPerSecond
        self.enableBatteryLevel = enableBatteryLevel
        self.lifecycleCapture = lifecycleCapture
        self.sessionJpegCapture = sessionJpegCapture
        self.touchCapture = touchCapture
        self.toolGuard = toolGuard
        self.customProperties = customProperties
        self.hostAutoProbe = hostAutoProbe
        self.hostConnection = hostConnection
    }

    public static func createBuilder() -> AnsightOptionsBuilder {
        AnsightOptionsBuilder()
    }

    public static func createBuilder(_ options: AnsightOptions) -> AnsightOptionsBuilder {
        AnsightOptionsBuilder(options)
    }

    public var maximumBufferSize: Int {
        retentionPeriodSeconds * Int(ceil(1000.0 / Double(sampleFrequencyMilliseconds)))
    }

    public func validated() throws -> AnsightOptions {
        var copy = self
        copy.sampleFrequencyMilliseconds = max(
            AnsightSamplingLimits.minSampleFrequencyMilliseconds,
            min(copy.sampleFrequencyMilliseconds, AnsightSamplingLimits.maxSampleFrequencyMilliseconds)
        )
        copy.retentionPeriodSeconds = max(
            AnsightSamplingLimits.minRetentionPeriodSeconds,
            min(copy.retentionPeriodSeconds, AnsightSamplingLimits.maxRetentionPeriodSeconds)
        )

        for channel in copy.additionalChannels {
            if AnsightChannels.reservedIds.contains(channel.id) {
                throw RuntimeError.invalidInput("Additional channel '\(channel.name)' uses reserved channel id \(channel.id).")
            }
            if !(0...255).contains(channel.id) {
                throw RuntimeError.invalidInput("Channel ids must be between 0 and 255.")
            }
            if channel.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                throw RuntimeError.invalidInput("Channel names must not be blank.")
            }
        }

        try copy.toolGuard.validate()
        copy.lifecycleCapture.validate()
        copy.sessionJpegCapture?.validate()
        copy.touchCapture?.validate()
        copy.hostAutoProbe.validate()
        try copy.hostConnection.validate()
        return copy
    }
}
