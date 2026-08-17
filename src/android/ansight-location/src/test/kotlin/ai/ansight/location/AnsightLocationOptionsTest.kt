package ai.ansight.location

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AnsightLocationOptionsTest {
    @Test
    fun captureIsDisabledByDefault() {
        val options = AnsightLocationOptions()

        assertFalse(options.enabled)
        assertEquals(5, options.decimalPlaces)
        assertEquals(1_000, options.minimumIntervalMilliseconds)
        assertEquals(1.0, options.minimumDistanceMeters, 0.0)
    }

    @Test
    fun enabledOptionsClampPrecisionAndSamplingControls() {
        val options = AnsightLocationOptions.enabled(
            decimalPlaces = 20,
            minimumIntervalMilliseconds = -1,
            minimumDistanceMeters = Double.NaN,
        )

        assertTrue(options.enabled)
        assertEquals(7, options.decimalPlaces)
        assertEquals(0, options.minimumIntervalMilliseconds)
        assertEquals(0.0, options.minimumDistanceMeters, 0.0)
    }
}
