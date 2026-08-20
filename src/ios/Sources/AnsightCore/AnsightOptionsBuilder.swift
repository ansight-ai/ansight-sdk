import Foundation

public final class AnsightOptionsBuilder {
    private var options: AnsightOptions

    public init(_ options: AnsightOptions = AnsightOptions()) {
        self.options = options
    }

    @discardableResult
    public func withSampleFrequencyMilliseconds(_ sampleFrequencyMilliseconds: Int) -> AnsightOptionsBuilder {
        options.sampleFrequencyMilliseconds = sampleFrequencyMilliseconds
        return self
    }

    @discardableResult
    public func withFramesPerSecond() -> AnsightOptionsBuilder {
        options.enableFramesPerSecond = true
        return self
    }

    @discardableResult
    public func withoutFramesPerSecond() -> AnsightOptionsBuilder {
        options.enableFramesPerSecond = false
        return self
    }

    @discardableResult
    public func withBatteryLevel() -> AnsightOptionsBuilder {
        options.enableBatteryLevel = true
        return self
    }

    @discardableResult
    public func withoutBatteryLevel() -> AnsightOptionsBuilder {
        options.enableBatteryLevel = false
        return self
    }

    @discardableResult
    public func withOpenFileHandleTracking() -> AnsightOptionsBuilder {
        options.enableOpenFileHandleTracking = true
        return self
    }

    @discardableResult
    public func withoutOpenFileHandleTracking() -> AnsightOptionsBuilder {
        options.enableOpenFileHandleTracking = false
        return self
    }

    @discardableResult
    public func withRetentionPeriodSeconds(_ retentionPeriodSeconds: Int) -> AnsightOptionsBuilder {
        options.retentionPeriodSeconds = retentionPeriodSeconds
        return self
    }

    @discardableResult
    public func withAdditionalChannels(_ additionalChannels: [AnsightChannel]) -> AnsightOptionsBuilder {
        options.additionalChannels = additionalChannels
        return self
    }

    @discardableResult
    public func addAdditionalChannel(_ additionalChannel: AnsightChannel) -> AnsightOptionsBuilder {
        options.additionalChannels.append(additionalChannel)
        return self
    }

    @discardableResult
    public func withDefaultMemoryChannels(_ memoryChannels: DefaultMemoryChannels) -> AnsightOptionsBuilder {
        options.defaultMemoryChannels = memoryChannels
        return self
    }

    @discardableResult
    public func withoutDefaultMemoryChannels(_ memoryChannels: DefaultMemoryChannels) -> AnsightOptionsBuilder {
        options.defaultMemoryChannels.subtract(memoryChannels)
        return self
    }

    @discardableResult
    public func withLifecycleCapture(_ lifecycleCapture: AnsightLifecycleCaptureOptions) -> AnsightOptionsBuilder {
        options.lifecycleCapture = lifecycleCapture
        return self
    }

    @discardableResult
    public func withSessionJpegCapture(
        intervalMilliseconds: Int = AnsightSessionJpegCaptureOptions.defaultIntervalMilliseconds,
        quality: Int = AnsightSessionJpegCaptureOptions.defaultQuality,
        maxWidth: Int? = AnsightSessionJpegCaptureOptions.defaultMaxWidth,
        captureGpuBackedSurfaces: Bool = AnsightSessionJpegCaptureOptions.defaultCaptureGpuBackedSurfaces,
        mode: AnsightSessionJpegCaptureMode = AnsightSessionJpegCaptureOptions.defaultMode,
        captureKeyboardPresence: Bool = AnsightSessionJpegCaptureOptions.defaultCaptureKeyboardPresence
    ) -> AnsightOptionsBuilder {
        options.sessionJpegCapture = AnsightSessionJpegCaptureOptions(
            intervalMilliseconds: intervalMilliseconds,
            quality: quality,
            maxWidth: maxWidth,
            captureGpuBackedSurfaces: captureGpuBackedSurfaces,
            mode: mode,
            captureKeyboardPresence: captureKeyboardPresence
        )
        return self
    }

    @discardableResult
    public func withSessionJpegCapture(_ sessionJpegCapture: AnsightSessionJpegCaptureOptions) -> AnsightOptionsBuilder {
        options.sessionJpegCapture = sessionJpegCapture
        return self
    }

    @discardableResult
    public func withoutSessionJpegCapture() -> AnsightOptionsBuilder {
        options.sessionJpegCapture = nil
        return self
    }

    @discardableResult
    public func withTouchCapture(
        captureMoveEvents: Bool = true,
        captureCancelEvents: Bool = true,
        moveCaptureDistanceThreshold: Double = AnsightTouchCaptureOptions.defaultMoveCaptureDistanceThreshold,
        moveCaptureFramesPerSecond: Int = AnsightTouchCaptureOptions.defaultMoveCaptureFramesPerSecond
    ) -> AnsightOptionsBuilder {
        options.touchCapture = AnsightTouchCaptureOptions(
            captureMoveEvents: captureMoveEvents,
            captureCancelEvents: captureCancelEvents,
            moveCaptureDistanceThreshold: moveCaptureDistanceThreshold,
            moveCaptureFramesPerSecond: moveCaptureFramesPerSecond
        )
        return self
    }

    @discardableResult
    public func withTouchCapture(_ touchCapture: AnsightTouchCaptureOptions) -> AnsightOptionsBuilder {
        options.touchCapture = touchCapture
        return self
    }

    @discardableResult
    public func withoutTouchCapture() -> AnsightOptionsBuilder {
        options.touchCapture = nil
        return self
    }

    @discardableResult
    public func withCrashCapture(_ crashCapture: AnsightCrashCaptureOptions = AnsightCrashCaptureOptions()) -> AnsightOptionsBuilder {
        options.crashCapture = crashCapture
        options.crashCapture.enabled = true
        return self
    }

    @discardableResult
    public func withoutCrashCapture() -> AnsightOptionsBuilder {
        options.crashCapture.enabled = false
        return self
    }

    @discardableResult
    public func withToolGuard(_ toolGuard: AnsightToolGuard) -> AnsightOptionsBuilder {
        options.toolGuard = toolGuard
        return self
    }

    @discardableResult
    public func withToolsDisabled() -> AnsightOptionsBuilder {
        withToolGuard(.disabled)
    }

    @discardableResult
    public func withReadOnlyToolAccess() -> AnsightOptionsBuilder {
        withToolGuard(.readOnly)
    }

    @discardableResult
    public func withReadWriteToolAccess() -> AnsightOptionsBuilder {
        withToolGuard(.readWrite)
    }

    @discardableResult
    public func withAllToolAccess() -> AnsightOptionsBuilder {
        withToolGuard(.fullAccess)
    }

    @discardableResult
    public func registerCustomProperty(_ group: String, _ key: String, _ value: Any?) -> AnsightOptionsBuilder {
        let normalizedGroup = group.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedKey = key.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedGroup.isEmpty, !normalizedKey.isEmpty else {
            return self
        }

        var groupProperties = options.customProperties[normalizedGroup] ?? [:]
        groupProperties[normalizedKey] = String(describing: value ?? "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        options.customProperties[normalizedGroup] = groupProperties
        return self
    }

    @discardableResult
    public func removeCustomProperty(_ group: String, _ key: String) -> AnsightOptionsBuilder {
        let normalizedGroup = group.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedKey = key.trimmingCharacters(in: .whitespacesAndNewlines)
        options.customProperties[normalizedGroup]?.removeValue(forKey: normalizedKey)
        if options.customProperties[normalizedGroup]?.isEmpty == true {
            options.customProperties.removeValue(forKey: normalizedGroup)
        }

        return self
    }

    @discardableResult
    public func clearCustomProperties() -> AnsightOptionsBuilder {
        options.customProperties.removeAll()
        return self
    }

    @discardableResult
    public func withHostAutoProbe(_ hostAutoProbe: AnsightHostAutoProbeOptions = .enabledDefault) -> AnsightOptionsBuilder {
        options.hostAutoProbe = hostAutoProbe
        options.hostAutoProbe.enabled = true
        return self
    }

    @discardableResult
    public func withoutHostAutoProbe() -> AnsightOptionsBuilder {
        options.hostAutoProbe = .disabledDefault
        return self
    }

    @discardableResult
    public func withHostConnection(_ hostConnection: AnsightHostConnectionOptions = AnsightHostConnectionOptions()) -> AnsightOptionsBuilder {
        options.hostConnection = hostConnection
        return self
    }

    @discardableResult
    public func configureHostConnection(_ configure: (inout AnsightHostConnectionOptions) -> Void) -> AnsightOptionsBuilder {
        configure(&options.hostConnection)
        return self
    }

    @discardableResult
    public func withBundledHostConnection(
        bundledConfigJson: String? = nil
    ) -> AnsightOptionsBuilder {
        configureHostConnection { hostConnection in
            hostConnection.bundledConfigJson = bundledConfigJson
        }
    }

    @discardableResult
    public func withHostConnectionDiscoveryPort(_ discoveryPort: Int) -> AnsightOptionsBuilder {
        configureHostConnection { hostConnection in
            hostConnection.discoveryPort = discoveryPort
        }
    }

    @discardableResult
    public func withCellularHostConnections(_ allow: Bool = true) -> AnsightOptionsBuilder {
        configureHostConnection { hostConnection in
            hostConnection.allowCellularConnections = allow
        }
    }

    @discardableResult
    public func withUnattendedProvisioning(_ allow: Bool = true) -> AnsightOptionsBuilder {
        configureHostConnection { hostConnection in
            hostConnection.allowUnattendedProvisioning = allow
        }
    }

    @discardableResult
    public func withoutUnattendedProvisioning() -> AnsightOptionsBuilder {
        withUnattendedProvisioning(false)
    }

    @discardableResult
    public func withHostConnectionProfileRetentionSeconds(_ retentionSeconds: Int) -> AnsightOptionsBuilder {
        configureHostConnection { hostConnection in
            hostConnection.connectionProfileRetentionSeconds = retentionSeconds
        }
    }

    public func build() throws -> AnsightOptions {
        try options.validated()
    }
}
