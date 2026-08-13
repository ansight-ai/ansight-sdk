import Foundation

public struct AnsightOptions: Sendable, Codable, Equatable {
    public var sampleFrequencyMilliseconds: Int
    public var retentionPeriodSeconds: Int
    public var additionalChannels: [AnsightChannel]
    public var defaultMemoryChannels: DefaultMemoryChannels
    public var enableFramesPerSecond: Bool
    public var enableBatteryLevel: Bool
    public var enableOpenFileHandleTracking: Bool
    public var lifecycleCapture: AnsightLifecycleCaptureOptions
    public var sessionJpegCapture: AnsightSessionJpegCaptureOptions?
    public var touchCapture: AnsightTouchCaptureOptions?
    public var toolGuard: AnsightToolGuard
    public var customProperties: [String: [String: String]]
    public var hostAutoProbe: AnsightHostAutoProbeOptions
    public var hostConnection: AnsightHostConnectionOptions
    public var crashCapture: AnsightCrashCaptureOptions

    public init(
        sampleFrequencyMilliseconds: Int = AnsightSamplingLimits.defaultSampleFrequencyMilliseconds,
        retentionPeriodSeconds: Int = AnsightSamplingLimits.defaultRetentionPeriodSeconds,
        additionalChannels: [AnsightChannel] = [],
        defaultMemoryChannels: DefaultMemoryChannels = .platformDefaults,
        enableFramesPerSecond: Bool = true,
        enableBatteryLevel: Bool = false,
        enableOpenFileHandleTracking: Bool = false,
        lifecycleCapture: AnsightLifecycleCaptureOptions = .enabledDefault,
        sessionJpegCapture: AnsightSessionJpegCaptureOptions? = nil,
        touchCapture: AnsightTouchCaptureOptions? = AnsightTouchCaptureOptions(),
        toolGuard: AnsightToolGuard = .disabled,
        customProperties: [String: [String: String]] = [:],
        hostAutoProbe: AnsightHostAutoProbeOptions = .enabledDefault,
        hostConnection: AnsightHostConnectionOptions = AnsightHostConnectionOptions(),
        crashCapture: AnsightCrashCaptureOptions = AnsightCrashCaptureOptions()
    ) {
        self.sampleFrequencyMilliseconds = sampleFrequencyMilliseconds
        self.retentionPeriodSeconds = retentionPeriodSeconds
        self.additionalChannels = additionalChannels
        self.defaultMemoryChannels = defaultMemoryChannels
        self.enableFramesPerSecond = enableFramesPerSecond
        self.enableBatteryLevel = enableBatteryLevel
        self.enableOpenFileHandleTracking = enableOpenFileHandleTracking
        self.lifecycleCapture = lifecycleCapture
        self.sessionJpegCapture = sessionJpegCapture
        self.touchCapture = touchCapture
        self.toolGuard = toolGuard
        self.customProperties = customProperties
        self.hostAutoProbe = hostAutoProbe
        self.hostConnection = hostConnection
        self.crashCapture = crashCapture
    }

    private enum CodingKeys: String, CodingKey {
        case sampleFrequencyMilliseconds
        case retentionPeriodSeconds
        case additionalChannels
        case defaultMemoryChannels
        case enableFramesPerSecond
        case enableBatteryLevel
        case enableOpenFileHandleTracking
        case lifecycleCapture
        case sessionJpegCapture
        case touchCapture
        case toolGuard
        case customProperties
        case hostAutoProbe
        case hostConnection
        case crashCapture
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        sampleFrequencyMilliseconds = try container.decode(Int.self, forKey: .sampleFrequencyMilliseconds)
        retentionPeriodSeconds = try container.decode(Int.self, forKey: .retentionPeriodSeconds)
        additionalChannels = try container.decode([AnsightChannel].self, forKey: .additionalChannels)
        defaultMemoryChannels = try container.decode(DefaultMemoryChannels.self, forKey: .defaultMemoryChannels)
        enableFramesPerSecond = try container.decode(Bool.self, forKey: .enableFramesPerSecond)
        enableBatteryLevel = try container.decode(Bool.self, forKey: .enableBatteryLevel)
        enableOpenFileHandleTracking = try container.decodeIfPresent(
            Bool.self,
            forKey: .enableOpenFileHandleTracking
        ) ?? false
        lifecycleCapture = try container.decode(AnsightLifecycleCaptureOptions.self, forKey: .lifecycleCapture)
        sessionJpegCapture = try container.decodeIfPresent(
            AnsightSessionJpegCaptureOptions.self,
            forKey: .sessionJpegCapture
        )
        touchCapture = try container.decodeIfPresent(AnsightTouchCaptureOptions.self, forKey: .touchCapture)
        toolGuard = try container.decode(AnsightToolGuard.self, forKey: .toolGuard)
        customProperties = try container.decode([String: [String: String]].self, forKey: .customProperties)
        hostAutoProbe = try container.decode(AnsightHostAutoProbeOptions.self, forKey: .hostAutoProbe)
        hostConnection = try container.decode(AnsightHostConnectionOptions.self, forKey: .hostConnection)
        crashCapture = try container.decodeIfPresent(
            AnsightCrashCaptureOptions.self,
            forKey: .crashCapture
        ) ?? AnsightCrashCaptureOptions()
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
        copy.crashCapture.validate()
        return copy
    }
}
