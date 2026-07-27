package ai.ansight.runtime

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HostSessionJpegCapturePolicyTest {
    @Test
    fun hostModeDisablesSdkCapture() {
        val policy = HostSessionJpegCapturePolicy.fromPayload(
            JSONObject()
                .put(
                    "sessionJpegCapture",
                    JSONObject()
                        .put("mode", "host")
                        .put("source", "adb"),
                ),
        )

        assertTrue(policy.useHostCapture)
        assertEquals("adb", policy.source)
        assertEquals(1, HostSessionJpegCapturePolicy.ControlVersion)
        assertEquals(
            "sessionJpegCaptureControlVersion",
            HostSessionJpegCapturePolicy.ControlVersionPropertyName,
        )
    }

    @Test
    fun missingOrAppModeKeepsSdkCapture() {
        assertFalse(HostSessionJpegCapturePolicy.fromPayload(null).useHostCapture)
        assertFalse(
            HostSessionJpegCapturePolicy.fromPayload(
                JSONObject().put(
                    "sessionJpegCapture",
                    JSONObject().put("mode", "app"),
                ),
            ).useHostCapture,
        )
    }
}
