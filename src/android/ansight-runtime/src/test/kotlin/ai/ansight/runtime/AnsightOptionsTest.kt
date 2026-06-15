package ai.ansight.runtime

import org.junit.Assert.assertEquals
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
    fun validatedNormalizesNestedCustomProperties() {
        val validated = AnsightOptions(
            customProperties = mapOf(
                " runtime " to mapOf(
                    " sdk " to " android ",
                    " " to "ignored",
                ),
            ),
        ).validated()

        assertEquals("android", validated.customProperties["runtime"]?.get("sdk"))
        assertEquals(null, validated.customProperties["runtime"]?.get(""))
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
}
