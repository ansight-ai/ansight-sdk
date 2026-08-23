package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject

/** One captured HTTP header after sensitive-value redaction. */
data class AnsightNetworkHeader(
    val name: String,
    val value: String,
) {
    internal fun toJson(): JSONObject = JSONObject()
        .put("name", name)
        .put("value", value)

    companion object {
        internal fun fromJson(value: JSONObject): AnsightNetworkHeader? {
            val name = value.optString("name").trim()
            if (name.isEmpty()) return null
            return AnsightNetworkHeader(name, value.optString("value"))
        }
    }
}

/** Optional bounded request or response body. */
data class AnsightNetworkBody(
    val contentType: String? = null,
    val encoding: String,
    val data: String,
    val capturedBytes: Long,
    val totalBytes: Long? = null,
    val truncated: Boolean,
) {
    internal fun toJson(): JSONObject = JSONObject()
        .putOptional("contentType", contentType)
        .put("encoding", encoding)
        .put("data", data)
        .put("capturedBytes", capturedBytes)
        .putOptional("totalBytes", totalBytes)
        .put("truncated", truncated)

    companion object {
        internal fun fromJson(value: JSONObject?): AnsightNetworkBody? {
            value ?: return null
            return AnsightNetworkBody(
                contentType = value.networkOptionalString("contentType"),
                encoding = value.optString("encoding"),
                data = value.optString("data"),
                capturedBytes = value.optLong("capturedBytes"),
                totalBytes = value.networkOptionalLong("totalBytes"),
                truncated = value.optBoolean("truncated"),
            )
        }
    }
}

/**
 * Captured data for one completed HTTP request.
 *
 * Text bodies are included by default by capture integrations and bounded before transport.
 */
data class AnsightNetworkRequest(
    val id: String,
    val source: String,
    val startedAtUtc: String,
    val completedAtUtc: String,
    val durationMilliseconds: Double,
    val method: String,
    val url: String,
    val protocol: String? = null,
    val requestHeaders: List<AnsightNetworkHeader> = emptyList(),
    val requestBodySizeBytes: Long? = null,
    val requestBody: AnsightNetworkBody? = null,
    val statusCode: Int? = null,
    val reasonPhrase: String? = null,
    val responseHeaders: List<AnsightNetworkHeader> = emptyList(),
    val responseBodySizeBytes: Long? = null,
    val responseBody: AnsightNetworkBody? = null,
    val errorType: String? = null,
    val errorMessage: String? = null,
    val schema: String = SchemaName,
) {
    internal fun toJson(): JSONObject = JSONObject()
        .put("schema", SchemaName)
        .put("id", id)
        .put("source", source)
        .put("startedAtUtc", startedAtUtc)
        .put("completedAtUtc", completedAtUtc)
        .put("durationMilliseconds", durationMilliseconds)
        .put("method", method)
        .put("url", url)
        .putOptional("protocol", protocol)
        .put("requestHeaders", JSONArray(requestHeaders.map(AnsightNetworkHeader::toJson)))
        .putOptional("requestBodySizeBytes", requestBodySizeBytes)
        .putOptional("requestBody", requestBody?.toJson())
        .putOptional("statusCode", statusCode)
        .putOptional("reasonPhrase", reasonPhrase)
        .put("responseHeaders", JSONArray(responseHeaders.map(AnsightNetworkHeader::toJson)))
        .putOptional("responseBodySizeBytes", responseBodySizeBytes)
        .putOptional("responseBody", responseBody?.toJson())
        .putOptional("errorType", errorType)
        .putOptional("errorMessage", errorMessage)

    companion object {
        const val SchemaName = "ansight.network-request.v1"

        fun fromJson(value: JSONObject): AnsightNetworkRequest? {
            if (value.optString("schema") != SchemaName) return null
            return AnsightNetworkRequest(
                id = value.optString("id"),
                source = value.optString("source"),
                startedAtUtc = value.optString("startedAtUtc"),
                completedAtUtc = value.optString("completedAtUtc"),
                durationMilliseconds = value.optDouble("durationMilliseconds", 0.0),
                method = value.optString("method"),
                url = value.optString("url"),
                protocol = value.networkOptionalString("protocol"),
                requestHeaders = value.headerList("requestHeaders"),
                requestBodySizeBytes = value.networkOptionalLong("requestBodySizeBytes"),
                requestBody = AnsightNetworkBody.fromJson(value.optJSONObject("requestBody")),
                statusCode = value.networkOptionalInt("statusCode"),
                reasonPhrase = value.networkOptionalString("reasonPhrase"),
                responseHeaders = value.headerList("responseHeaders"),
                responseBodySizeBytes = value.networkOptionalLong("responseBodySizeBytes"),
                responseBody = AnsightNetworkBody.fromJson(value.optJSONObject("responseBody")),
                errorType = value.networkOptionalString("errorType"),
                errorMessage = value.networkOptionalString("errorMessage"),
            )
        }
    }
}

private fun JSONObject.headerList(key: String): List<AnsightNetworkHeader> {
    val values = optJSONArray(key) ?: return emptyList()
    return buildList {
        for (index in 0 until values.length()) {
            values.optJSONObject(index)?.let(AnsightNetworkHeader::fromJson)?.let(::add)
        }
    }
}

private fun JSONObject.networkOptionalString(key: String): String? =
    takeIf { has(key) && !isNull(key) }?.optString(key)?.takeIf(String::isNotBlank)

private fun JSONObject.networkOptionalLong(key: String): Long? =
    takeIf { has(key) && !isNull(key) }?.optLong(key)

private fun JSONObject.networkOptionalInt(key: String): Int? =
    takeIf { has(key) && !isNull(key) }?.optInt(key)

private fun JSONObject.putOptional(key: String, value: Any?): JSONObject = apply {
    if (value != null) put(key, value)
}
