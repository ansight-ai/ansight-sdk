package ai.ansight.runtime

internal object PairingRedaction {
    private val querySecret = Regex("(?i)([?&](?:token|secret|proof|signature|grant)=)[^&\\s]+")
    private val jsonSecret = Regex("(?i)(\"(?:oneTimeToken|secret|proof|signature)\"\\s*:\\s*\")[^\"]*(\")")

    fun redact(message: String): String {
        return jsonSecret.replace(querySecret.replace(message) { match -> "${match.groupValues[1]}<redacted>" }) { match ->
            "${match.groupValues[1]}<redacted>${match.groupValues[2]}"
        }
    }
}
