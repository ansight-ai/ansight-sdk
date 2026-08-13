package ai.ansight.runtime

class AnsightOptionsBuilder @JvmOverloads constructor(
    initialOptions: AnsightOptions = AnsightOptions(),
) {
    private var options: AnsightOptions = initialOptions

    fun withSampleFrequencyMilliseconds(sampleFrequencyMilliseconds: Int): AnsightOptionsBuilder {
        options = options.copy(sampleFrequencyMilliseconds = sampleFrequencyMilliseconds)
        return this
    }

    fun withFramesPerSecond(): AnsightOptionsBuilder {
        options = options.copy(enableFramesPerSecond = true)
        return this
    }

    fun withoutFramesPerSecond(): AnsightOptionsBuilder {
        options = options.copy(enableFramesPerSecond = false)
        return this
    }

    fun withBatteryLevel(): AnsightOptionsBuilder {
        options = options.copy(enableBatteryLevel = true)
        return this
    }

    fun withoutBatteryLevel(): AnsightOptionsBuilder {
        options = options.copy(enableBatteryLevel = false)
        return this
    }

    fun withOpenFileHandleTracking(): AnsightOptionsBuilder {
        options = options.copy(enableOpenFileHandleTracking = true)
        return this
    }

    fun withoutOpenFileHandleTracking(): AnsightOptionsBuilder {
        options = options.copy(enableOpenFileHandleTracking = false)
        return this
    }

    fun withJniReferenceCountTracking(): AnsightOptionsBuilder {
        options = options.copy(enableJniReferenceCountTracking = true)
        return this
    }

    fun withoutJniReferenceCountTracking(): AnsightOptionsBuilder {
        options = options.copy(enableJniReferenceCountTracking = false)
        return this
    }

    fun withRetentionPeriodSeconds(retentionPeriodSeconds: Int): AnsightOptionsBuilder {
        options = options.copy(retentionPeriodSeconds = retentionPeriodSeconds)
        return this
    }

    fun withAdditionalChannels(additionalChannels: Iterable<AnsightChannel>): AnsightOptionsBuilder {
        options = options.copy(additionalChannels = additionalChannels.toList())
        return this
    }

    fun addAdditionalChannel(additionalChannel: AnsightChannel): AnsightOptionsBuilder {
        options = options.copy(additionalChannels = options.additionalChannels + additionalChannel)
        return this
    }

    fun withDefaultMemoryChannels(memoryChannels: DefaultMemoryChannels): AnsightOptionsBuilder {
        options = options.copy(defaultMemoryChannels = memoryChannels)
        return this
    }

    fun withoutDefaultMemoryChannels(memoryChannels: DefaultMemoryChannels): AnsightOptionsBuilder {
        options = options.copy(
            defaultMemoryChannels = options.defaultMemoryChannels.copy(
                javaHeap = options.defaultMemoryChannels.javaHeap && !memoryChannels.javaHeap,
                nativeHeap = options.defaultMemoryChannels.nativeHeap && !memoryChannels.nativeHeap,
                rss = options.defaultMemoryChannels.rss && !memoryChannels.rss,
            ),
        )
        return this
    }

    @JvmOverloads
    fun withSessionJpegCapture(
        intervalMilliseconds: Int = AnsightSessionJpegCaptureOptions.DefaultIntervalMilliseconds,
        quality: Int = AnsightSessionJpegCaptureOptions.DefaultQuality,
        maxWidth: Int? = AnsightSessionJpegCaptureOptions.DefaultMaxWidth,
        captureGpuBackedSurfaces: Boolean = AnsightSessionJpegCaptureOptions.DefaultCaptureGpuBackedSurfaces,
        mode: AnsightSessionJpegCaptureMode = AnsightSessionJpegCaptureOptions.DefaultMode,
    ): AnsightOptionsBuilder {
        options = options.copy(
            sessionJpegCapture = AnsightSessionJpegCaptureOptions(
                intervalMilliseconds = intervalMilliseconds,
                quality = quality,
                maxWidth = maxWidth,
                captureGpuBackedSurfaces = captureGpuBackedSurfaces,
                mode = mode,
            ),
        )
        return this
    }

    fun withSessionJpegCapture(sessionJpegCapture: AnsightSessionJpegCaptureOptions): AnsightOptionsBuilder {
        options = options.copy(sessionJpegCapture = sessionJpegCapture)
        return this
    }

    fun withoutSessionJpegCapture(): AnsightOptionsBuilder {
        options = options.copy(sessionJpegCapture = null)
        return this
    }

    @JvmOverloads
    fun withTouchCapture(
        moveCaptureDistanceThreshold: Double = 8.0,
        moveCaptureFramesPerSecond: Int = 20,
    ): AnsightOptionsBuilder {
        options = options.copy(
            touchCapture = AnsightTouchCaptureOptions(
                moveCaptureDistanceThreshold = moveCaptureDistanceThreshold,
                moveCaptureFramesPerSecond = moveCaptureFramesPerSecond,
            ),
        )
        return this
    }

    fun withTouchCapture(touchCapture: AnsightTouchCaptureOptions): AnsightOptionsBuilder {
        options = options.copy(touchCapture = touchCapture)
        return this
    }

    fun withoutTouchCapture(): AnsightOptionsBuilder {
        options = options.copy(touchCapture = null)
        return this
    }

    @JvmOverloads
    fun withCrashCapture(crashCapture: AnsightCrashCaptureOptions = AnsightCrashCaptureOptions()): AnsightOptionsBuilder {
        options = options.copy(crashCapture = crashCapture.copy(enabled = true))
        return this
    }

    fun withoutCrashCapture(): AnsightOptionsBuilder {
        options = options.copy(crashCapture = options.crashCapture.copy(enabled = false))
        return this
    }

    fun withToolGuard(toolGuard: AnsightToolGuard): AnsightOptionsBuilder {
        options = options.copy(toolGuard = toolGuard)
        return this
    }

    fun withToolsDisabled(): AnsightOptionsBuilder = withToolGuard(AnsightToolGuard.Disabled)

    fun withReadOnlyToolAccess(): AnsightOptionsBuilder = withToolGuard(AnsightToolGuard.ReadOnly)

    fun withReadWriteToolAccess(): AnsightOptionsBuilder = withToolGuard(AnsightToolGuard.ReadWrite)

    fun withAllToolAccess(): AnsightOptionsBuilder = withToolGuard(AnsightToolGuard.FullAccess)

    fun registerCustomProperty(group: String, key: String, value: Any?): AnsightOptionsBuilder {
        val normalizedGroup = group.trim()
        val normalizedKey = key.trim()
        if (normalizedGroup.isBlank() || normalizedKey.isBlank()) {
            return this
        }

        val groups = options.customProperties.mapValues { it.value.toMutableMap() }.toMutableMap()
        val properties = groups.getOrPut(normalizedGroup) { mutableMapOf() }
        properties[normalizedKey] = value?.toString()?.trim().orEmpty()
        options = options.copy(customProperties = groups)
        return this
    }

    fun removeCustomProperty(group: String, key: String): AnsightOptionsBuilder {
        val normalizedGroup = group.trim()
        val normalizedKey = key.trim()
        val groups = options.customProperties.mapValues { it.value.toMutableMap() }.toMutableMap()
        groups[normalizedGroup]?.remove(normalizedKey)
        if (groups[normalizedGroup]?.isEmpty() == true) {
            groups.remove(normalizedGroup)
        }

        options = options.copy(customProperties = groups)
        return this
    }

    fun clearCustomProperties(): AnsightOptionsBuilder {
        options = options.copy(customProperties = emptyMap())
        return this
    }

    @JvmOverloads
    fun withHostAutoProbe(hostAutoProbe: AnsightHostAutoProbeOptions = AnsightHostAutoProbeOptions()): AnsightOptionsBuilder {
        options = options.copy(hostAutoProbe = hostAutoProbe.copy(enabled = true))
        return this
    }

    fun withoutHostAutoProbe(): AnsightOptionsBuilder {
        options = options.copy(hostAutoProbe = options.hostAutoProbe.copy(enabled = false))
        return this
    }

    @JvmOverloads
    fun withHostConnection(hostConnection: AnsightHostConnectionOptions = AnsightHostConnectionOptions()): AnsightOptionsBuilder {
        options = options.copy(hostConnection = hostConnection)
        return this
    }

    fun configureHostConnection(configure: (AnsightHostConnectionOptions) -> AnsightHostConnectionOptions): AnsightOptionsBuilder {
        options = options.copy(hostConnection = configure(options.hostConnection))
        return this
    }

    @JvmOverloads
    fun withBundledHostConnection(
        bundledConfigJson: String? = null,
    ): AnsightOptionsBuilder {
        return configureHostConnection { hostConnection ->
            hostConnection.copy(
                bundledConfigJson = bundledConfigJson,
            )
        }
    }

    fun withHostConnectionDiscoveryPort(discoveryPort: Int): AnsightOptionsBuilder {
        return configureHostConnection { hostConnection ->
            hostConnection.copy(discoveryPort = discoveryPort)
        }
    }

    @JvmOverloads
    fun withCellularHostConnections(allow: Boolean = true): AnsightOptionsBuilder {
        return configureHostConnection { hostConnection ->
            hostConnection.copy(allowCellularConnections = allow)
        }
    }

    fun withHostConnectionProfileRetentionSeconds(retentionSeconds: Long): AnsightOptionsBuilder {
        return configureHostConnection { hostConnection ->
            hostConnection.copy(connectionProfileRetentionSeconds = retentionSeconds)
        }
    }

    fun withHostConnectionConfigReader(configReader: HostConnectionConfigReader?): AnsightOptionsBuilder {
        return configureHostConnection { hostConnection ->
            hostConnection.copy(configReader = configReader)
        }
    }

    fun withSecureStorage(secureStorage: AnsightSecureStorageOptions): AnsightOptionsBuilder {
        options = options.copy(secureStorage = secureStorage)
        return this
    }

    fun withTools(tools: Iterable<AndroidTool>): AnsightOptionsBuilder {
        options = options.copy(initialTools = tools.toList())
        return this
    }

    fun addTool(tool: AndroidTool): AnsightOptionsBuilder {
        options = options.copy(initialTools = options.initialTools + tool)
        return this
    }

    fun addTools(tools: Iterable<AndroidTool>): AnsightOptionsBuilder {
        options = options.copy(initialTools = options.initialTools + tools)
        return this
    }

    fun containsTool(toolId: String): Boolean {
        val normalized = toolId.trim()
        return options.initialTools.any { it.definition.id == normalized }
    }

    fun withArtifactProviders(providers: Iterable<AndroidArtifactProvider>): AnsightOptionsBuilder {
        options = options.copy(artifactProviders = providers.toList())
        return this
    }

    fun addArtifactProvider(provider: AndroidArtifactProvider): AnsightOptionsBuilder {
        options = options.copy(artifactProviders = options.artifactProviders + provider)
        return this
    }

    fun addArtifactProviders(providers: Iterable<AndroidArtifactProvider>): AnsightOptionsBuilder {
        options = options.copy(artifactProviders = options.artifactProviders + providers)
        return this
    }

    fun containsArtifactProvider(providerId: String): Boolean {
        val normalized = providerId.trim()
        return options.artifactProviders.any { it.descriptor.id == normalized }
    }

    fun build(): AnsightOptions = options.validated()
}
