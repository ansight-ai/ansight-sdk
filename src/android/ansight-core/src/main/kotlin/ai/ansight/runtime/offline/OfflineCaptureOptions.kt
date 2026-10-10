package ai.ansight.runtime

/** Native capture policy, shared by native Android and framework SDKs. */
data class OfflineCaptureOptions @JvmOverloads constructor(
    val enabled: Boolean = false,
    val transferMode: OfflineCaptureTransferMode = OfflineCaptureTransferMode.Automatic,
    val maximumSessions: Int = 10,
    val maximumSessionBytes: Long = 128L * 1024 * 1024,
    val maximumRetainedBytes: Long = 512L * 1024 * 1024,
    val maximumQueuedBytes: Int = 8 * 1024 * 1024,
) {
    fun validated(): OfflineCaptureOptions {
        require(maximumSessions > 0 && maximumSessionBytes > 0 &&
            maximumRetainedBytes >= maximumSessionBytes && maximumQueuedBytes > 0) {
            "Offline capture requires positive limits and a retained-byte budget at least as large as one session."
        }
        return this
    }
}
