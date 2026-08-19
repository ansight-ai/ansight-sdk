package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class AnsightOptionsTest {
    @Test
    fun validatedClampsSamplingAndRetentionBounds() {
        val validated = AnsightOptions(
            sampleFrequencyMilliseconds = 10,
            retentionPeriodSeconds = 9_999,
        ).validated()

        assertEquals(AnsightSamplingLimits.MinSampleFrequencyMilliseconds, validated.sampleFrequencyMilliseconds)
        assertEquals(AnsightSamplingLimits.MaxRetentionPeriodSeconds, validated.retentionPeriodSeconds)
    }

    @Test
    fun validatedRejectsReservedAdditionalChannelIds() {
        assertThrows(IllegalArgumentException::class.java) {
            AnsightOptions(
                additionalChannels = listOf(
                    AnsightChannel(AnsightChannels.FramesPerSecond, "bad"),
                ),
            ).validated()
        }
    }

    @Test
    fun runtimeDiagnosticChannelIdsAreReserved() {
        assertTrue(AnsightChannels.JniReferenceCount in AnsightChannels.reservedIds)
        assertTrue(AnsightChannels.OpenFileHandles in AnsightChannels.reservedIds)
    }

    @Test
    fun metricStreamCarriesChannelMetadataAndSamplesCurrentValue() {
        var value = 58L
        val stream = AnsightMetricStream(
            AnsightChannel(42, "React Native JS FPS", "#61DAFB", "fps", "reactNative"),
            AnsightMetricSampler { value },
        )

        assertEquals(42, stream.channel.id)
        assertEquals("fps", stream.channel.unit)
        assertEquals("reactNative", stream.channel.type)
        assertEquals(58L, stream.sample())

        value = 43L
        assertEquals(43L, stream.sample())
    }

    @Test
    fun validatedNormalizesNestedCustomProperties() {
        val validated = AnsightOptions(
            customProperties = mapOf(
                " runtime " to mapOf(
                    " sdk " to " android ",
                    " " to "ignored",
                ),
                " empty " to mapOf(
                    " " to "ignored",
                ),
            ),
        ).validated()

        assertEquals("android", validated.customProperties["runtime"]?.get("sdk"))
        assertEquals(null, validated.customProperties["runtime"]?.get(""))
        assertEquals(false, validated.customProperties.containsKey("empty"))
    }

    @Test
    fun secureStorageDefaultsToDenyAllAndNormalizesAllowLists() {
        val defaultOptions = AnsightOptions().validated()
        assertEquals(false, defaultOptions.secureStorage.isAllowed("token"))

        val validated = AnsightOptions(
            secureStorage = AnsightSecureStorageOptions(
                allowedKeys = setOf(" token "),
                allowedPrefixes = setOf("debug."),
            ),
        ).validated()

        assertEquals(true, validated.secureStorage.isAllowed("token"))
        assertEquals(true, validated.secureStorage.isAllowed("debug.session"))
        assertEquals(false, validated.secureStorage.isAllowed("prod.session"))
    }

    @Test
    fun developerDefaultsMatchNativeAggregateDefaults() {
        val options = AnsightDeveloperMode.options(
            clientName = "Validation Client",
        ).validated()

        assertEquals(400, options.sampleFrequencyMilliseconds)
        assertEquals(120, options.retentionPeriodSeconds)
        assertEquals(true, options.enableFramesPerSecond)
        assertEquals(false, options.enableBatteryLevel)
        assertEquals(AnsightToolGuard.FullAccess, options.toolGuard)
        assertEquals(true, options.hostAutoProbe.enabled)
        assertEquals("Validation Client", options.hostAutoProbe.clientName)
        assertFalse(options.hostConnection.allowCellularConnections)
        assertEquals(2_000, options.sessionJpegCapture?.intervalMilliseconds)
        assertEquals(60, options.sessionJpegCapture?.quality)
        assertEquals(480, options.sessionJpegCapture?.maxWidth)
        assertEquals(true, options.sessionJpegCapture?.captureGpuBackedSurfaces)
        assertEquals(AnsightSessionJpegCaptureMode.ScreenshotOnly, options.sessionJpegCapture?.mode)
        assertNotNull(options.touchCapture)
    }

    @Test
    fun builderAppliesDotNetStyleOptionsConvention() {
        val tool = FunctionAndroidTool(
            ToolDefinition(
                id = "app.echo",
                name = "Echo",
                description = "Echoes input.",
                category = "app",
                scope = ToolScope.Read,
                keywords = "echo",
            ),
        ) { _, _ -> AndroidToolResult.success() }

        val options = AnsightOptions.createBuilder()
            .withSampleFrequencyMilliseconds(400)
            .withRetentionPeriodSeconds(120)
            .withoutFramesPerSecond()
            .withBatteryLevel()
            .withOpenFileHandleTracking()
            .withJniReferenceCountTracking()
            .withSessionJpegCapture()
            .withTouchCapture(moveCaptureFramesPerSecond = 12)
            .withReadWriteToolAccess()
            .registerCustomProperty(" runtime ", " sdk ", " android ")
            .withBundledHostConnection(bundledConfigJson = "{profile}")
            .withHostConnectionDiscoveryPort(45200)
            .withHostConnectionProfileRetentionSeconds(60)
            .withCellularHostConnections()
            .withUnattendedProvisioning()
            .addArtifactProvider(TestArtifactProvider())
            .addTool(tool)
            .build()

        assertEquals(400, options.sampleFrequencyMilliseconds)
        assertEquals(120, options.retentionPeriodSeconds)
        assertFalse(options.enableFramesPerSecond)
        assertTrue(options.enableBatteryLevel)
        assertTrue(options.enableOpenFileHandleTracking)
        assertTrue(options.enableJniReferenceCountTracking)
        assertEquals(2_000, options.sessionJpegCapture?.intervalMilliseconds)
        assertEquals(60, options.sessionJpegCapture?.quality)
        assertEquals(480, options.sessionJpegCapture?.maxWidth)
        assertEquals(true, options.sessionJpegCapture?.captureGpuBackedSurfaces)
        assertEquals(12, options.touchCapture?.moveCaptureFramesPerSecond)
        assertEquals(AnsightToolGuard.ReadWrite, options.toolGuard)
        assertEquals("android", options.customProperties["runtime"]?.get("sdk"))
        assertEquals("{profile}", options.hostConnection.bundledConfigJson)
        assertEquals(45200, options.hostConnection.discoveryPort)
        assertEquals(60, options.hostConnection.connectionProfileRetentionSeconds)
        assertTrue(options.hostConnection.allowCellularConnections)
        assertTrue(options.hostConnection.allowUnattendedProvisioning)
        assertTrue(options.initialTools.any { it.definition.id == "app.echo" })
        assertTrue(options.artifactProviders.any { it.descriptor.id == "app.report" })
    }

    @Test
    fun runtimeDiagnosticTrackingDefaultsOffAndCanBeDisabledAgain() {
        val defaults = AnsightOptions.createBuilder().build()
        assertFalse(defaults.enableOpenFileHandleTracking)
        assertFalse(defaults.enableJniReferenceCountTracking)

        val disabled = AnsightOptions.createBuilder()
            .withOpenFileHandleTracking()
            .withJniReferenceCountTracking()
            .withoutOpenFileHandleTracking()
            .withoutJniReferenceCountTracking()
            .build()

        assertFalse(disabled.enableOpenFileHandleTracking)
        assertFalse(disabled.enableJniReferenceCountTracking)
    }

    @Test
    fun unattendedProvisioningDefaultsOffAndNormalizesOnlyEnabledPayloads() {
        assertFalse(AnsightOptions().hostConnection.allowUnattendedProvisioning)
        assertEquals(null, AnsightUnattendedProvisioning.normalizePayload(false, "ans2:test"))
        assertEquals(null, AnsightUnattendedProvisioning.normalizePayload(true, "  "))
        assertEquals("ans2:test", AnsightUnattendedProvisioning.normalizePayload(true, "  ans2:test  "))

        val disabled = AnsightOptions.createBuilder()
            .withUnattendedProvisioning()
            .withoutUnattendedProvisioning()
            .build()
        assertFalse(disabled.hostConnection.allowUnattendedProvisioning)
    }

    @Test
    fun builderCanDisableGpuBackedSurfaceCapture() {
        val options = AnsightOptions.createBuilder()
            .withSessionJpegCapture(captureGpuBackedSurfaces = false)
            .build()

        assertEquals(false, options.sessionJpegCapture?.captureGpuBackedSurfaces)
    }

    @Test
    fun builderCanCaptureScreenshotAndVisualTree() {
        val options = AnsightOptions.createBuilder()
            .withSessionJpegCapture(mode = AnsightSessionJpegCaptureMode.ScreenshotAndVisualTree)
            .build()

        assertEquals(AnsightSessionJpegCaptureMode.ScreenshotAndVisualTree, options.sessionJpegCapture?.mode)
    }

    @Test
    fun builderCanCaptureVisualTreesOnTouch() {
        val options = AnsightOptions.createBuilder()
            .withSessionJpegCapture(mode = AnsightSessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch)
            .build()

        assertEquals(AnsightSessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch, options.sessionJpegCapture?.mode)
    }

    @Test
    fun crashCaptureDefaultsOnAndClampsDurableOutboxBounds() {
        val options = AnsightOptions.createBuilder()
            .withCrashCapture(
                AnsightCrashCaptureOptions(
                    maximumPendingReports = 100,
                    retentionDays = 0,
                    maximumBreadcrumbs = 1_000,
                    maximumTraceBytes = 1,
                ),
            )
            .build()

        assertTrue(options.crashCapture.enabled)
        assertEquals(32, options.crashCapture.maximumPendingReports)
        assertEquals(1, options.crashCapture.retentionDays)
        assertEquals(256, options.crashCapture.maximumBreadcrumbs)
        assertEquals(16 * 1024, options.crashCapture.maximumTraceBytes)
        assertFalse(AnsightOptions.createBuilder().withoutCrashCapture().build().crashCapture.enabled)
    }

    private class TestArtifactProvider : AndroidArtifactProvider {
        override val descriptor = AndroidArtifactProviderDescriptor(
            id = "app.report",
            name = "App Report",
            description = "Test report provider.",
        )

        override fun query(context: AndroidArtifactQueryContext): List<AndroidArtifactDefinition> = emptyList()

        override fun create(request: AndroidArtifactRequest): AndroidArtifactResult {
            val bytes = "hello".toByteArray()
            return AndroidArtifactResult(
                metadata = AndroidArtifactMetadata(
                    providerId = descriptor.id,
                    artifactId = request.artifactId,
                    name = "Report",
                    kind = "text",
                    mimeType = "text/plain",
                    fileName = "report.txt",
                    sizeBytes = bytes.size.toLong(),
                ),
                bytes = bytes,
            )
        }
    }
}
