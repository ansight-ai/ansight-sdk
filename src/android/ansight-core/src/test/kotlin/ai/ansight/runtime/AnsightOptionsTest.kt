package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertThrows
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
            bundledDeveloperConfigJson = "{}",
            clientName = "Validation Client",
        ).validated()

        assertEquals(400, options.sampleFrequencyMilliseconds)
        assertEquals(120, options.retentionPeriodSeconds)
        assertEquals(true, options.enableFramesPerSecond)
        assertEquals(false, options.enableBatteryLevel)
        assertEquals(AnsightToolGuard.Full, options.toolGuard)
        assertEquals(true, options.hostAutoProbe.enabled)
        assertEquals("Validation Client", options.hostAutoProbe.clientName)
        assertEquals("{}", options.hostConnection.bundledDeveloperConfigJson)
        assertEquals(500, options.sessionJpegCapture?.intervalMilliseconds)
        assertEquals(60, options.sessionJpegCapture?.quality)
        assertEquals(480, options.sessionJpegCapture?.maxWidth)
        assertNotNull(options.touchCapture)
    }
}
