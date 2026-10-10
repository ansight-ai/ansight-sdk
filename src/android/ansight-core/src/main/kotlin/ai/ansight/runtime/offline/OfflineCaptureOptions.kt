package ai.ansight.runtime

/** Native capture policy, shared by native Android and framework SDKs. */
data class OfflineCaptureOptions @JvmOverloads constructor(
    val enabled: Boolean = false,
    val recordingEnabled: Boolean = true,
    val rootDirectory: String? = null,
    val transferMode: OfflineCaptureTransferMode = OfflineCaptureTransferMode.Automatic,
    val maximumSessions: Int = 10,
    val maximumSessionBytes: Long = 128L * 1024 * 1024,
    val maximumRetainedBytes: Long = 512L * 1024 * 1024,
    val maximumQueuedBytes: Int = 8 * 1024 * 1024,
) {
    companion object {
        @JvmStatic fun fromJson(json: org.json.JSONObject?): OfflineCaptureOptions? = json?.let {
            val mode = it.optString("transferMode", "automatic")
            require(mode == "automatic" || mode == "manual") { "Unknown offline transfer mode." }
            OfflineCaptureOptions(
                enabled = it.optBoolean("enabled", false),
                recordingEnabled = it.optBoolean("recordingEnabled", true),
                rootDirectory = it.optString("rootDirectory").takeIf { path -> path.isNotBlank() },
                transferMode = if (mode == "manual") OfflineCaptureTransferMode.Manual else OfflineCaptureTransferMode.Automatic,
                maximumSessions = it.optInt("maximumSessions", 10),
                maximumSessionBytes = it.optLong("maximumSessionBytes", 128L * 1024 * 1024),
                maximumRetainedBytes = it.optLong("maximumRetainedBytes", 512L * 1024 * 1024),
                maximumQueuedBytes = it.optInt("maximumQueuedBytes", 8 * 1024 * 1024),
            ).validated()
        }
    }

    fun validated(): OfflineCaptureOptions {
        require(maximumSessions > 0 && maximumSessionBytes > 0 &&
            maximumRetainedBytes >= maximumSessionBytes && maximumQueuedBytes > 0) {
            "Offline capture requires positive limits and a retained-byte budget at least as large as one session."
        }
        return this
    }
}
