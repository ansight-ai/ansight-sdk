package ai.ansight.runtime

import org.json.JSONObject

internal data class HostSessionJpegCapturePolicy(
    val useHostCapture: Boolean,
    val source: String?,
) {
    companion object {
        const val ControlVersionPropertyName = "sessionJpegCaptureControlVersion"
        const val ControlVersion = 1
        val App = HostSessionJpegCapturePolicy(false, null)

        fun fromPayload(payload: JSONObject?): HostSessionJpegCapturePolicy {
            val capture = payload?.optJSONObject("sessionJpegCapture") ?: return App
            if (!capture.optString("mode").equals("host", ignoreCase = true)) {
                return App
            }

            return HostSessionJpegCapturePolicy(
                useHostCapture = true,
                source = capture.optionalString("source"),
            )
        }
    }
}
