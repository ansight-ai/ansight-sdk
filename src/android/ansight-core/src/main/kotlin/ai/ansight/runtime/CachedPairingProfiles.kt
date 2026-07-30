package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject

internal data class CachedPairingProfile(
    val networkKey: String,
    val payload: String,
    val hostAddress: String?,
    val wifiName: String?,
    val hostName: String?,
    val cachedAtEpochMs: Long,
    val expiresAtEpochMs: Long,
)

internal data class CachedPairingProfileLoadResult(
    val profiles: List<CachedPairingProfile>,
    val shouldRewrite: Boolean,
)

internal object CachedPairingProfilesCodec {
    const val UnknownNetworkKey = "wifi:<unknown>"

    private const val SchemaName = "ansight.cached-pairing-profiles.v1"

    fun load(
        profilesJson: String?,
        nowEpochMs: Long,
    ): CachedPairingProfileLoadResult {
        val normalizedJson = profilesJson?.trim()?.ifBlank { null }
        if (normalizedJson != null) {
            val parsed = parseProfilesJson(normalizedJson, nowEpochMs)
            if (parsed != null) {
                val profiles = sortProfiles(parsed.profiles.filter { it.expiresAtEpochMs > nowEpochMs })
                return CachedPairingProfileLoadResult(
                    profiles,
                    parsed.shouldRewrite || profiles.size != parsed.profiles.size,
                )
            }
        }

        return CachedPairingProfileLoadResult(
            emptyList(),
            shouldRewrite = normalizedJson != null,
        )
    }

    fun upsert(
        existingProfiles: List<CachedPairingProfile>,
        payload: String,
        hostAddress: String?,
        document: ParsedPairingDocument,
        nowEpochMs: Long,
        retentionMillis: Long,
    ): List<CachedPairingProfile> {
        val networkKey = resolveNetworkKey(document)
        val profile = CachedPairingProfile(
            networkKey = networkKey,
            payload = payload.trim(),
            hostAddress = normalizedString(hostAddress),
            wifiName = normalizedString(document.discoveryHint?.wifiName),
            hostName = normalizedString(document.discoveryHint?.hostName),
            cachedAtEpochMs = nowEpochMs,
            expiresAtEpochMs = addSaturated(nowEpochMs, retentionMillis.coerceAtLeast(1_000L)),
        )
        val retainedProfiles = existingProfiles.filter {
            it.expiresAtEpochMs > nowEpochMs && !it.networkKey.equals(networkKey, ignoreCase = true)
        }
        return sortProfiles(retainedProfiles + profile)
    }

    fun remove(profiles: List<CachedPairingProfile>, networkKey: String): List<CachedPairingProfile> {
        return sortProfiles(profiles.filterNot { it.networkKey.equals(networkKey, ignoreCase = true) })
    }

    fun serialize(profiles: List<CachedPairingProfile>): String? {
        val sorted = sortProfiles(profiles)
        if (sorted.isEmpty()) {
            return null
        }

        val entries = JSONArray()
        for (profile in sorted) {
            entries.put(
                JSONObject()
                    .put("networkKey", profile.networkKey)
                    .putIfNotNull("wifiName", profile.wifiName)
                    .putIfNotNull("hostName", profile.hostName)
                    .put("cachedAtEpochMs", profile.cachedAtEpochMs)
                    .put("expiresAtEpochMs", profile.expiresAtEpochMs)
                    .put("payload", profile.payload)
                    .putIfNotNull("hostAddress", profile.hostAddress),
            )
        }

        return JSONObject()
            .put("schema", SchemaName)
            .put("profiles", entries)
            .toString()
    }

    fun resolveNetworkKey(document: ParsedPairingDocument): String {
        val wifiName = normalizedString(document.discoveryHint?.wifiName)
        return if (wifiName == null) UnknownNetworkKey else "wifi:$wifiName"
    }

    private fun parseProfilesJson(json: String, nowEpochMs: Long): ParsedProfiles? {
        return runCatching {
            val root = JSONObject(json)
            val entries = root.optJSONArray("profiles") ?: JSONArray()
            val profiles = mutableListOf<CachedPairingProfile>()
            var shouldRewrite = root.optionalString("schema") != SchemaName

            for (index in 0 until entries.length()) {
                val profile = runCatching { parseProfile(entries.optJSONObject(index), nowEpochMs) }.getOrNull()
                if (profile == null) {
                    shouldRewrite = true
                } else {
                    profiles.add(profile)
                }
            }

            ParsedProfiles(profiles, shouldRewrite)
        }.getOrNull()
    }

    private fun parseProfile(json: JSONObject?, nowEpochMs: Long): CachedPairingProfile? {
        if (json == null) {
            return null
        }

        val payload = json.optionalString("payload") ?: return null
        val networkKey = normalizedString(json.optionalString("networkKey"))
            ?: resolveNetworkKey(PairingConfigDocumentService.parseDocument(payload))
        val cachedAtEpochMs = optionalLong(json, "cachedAtEpochMs")?.takeIf { it > 0L } ?: nowEpochMs
        val expiresAtEpochMs = optionalLong(json, "expiresAtEpochMs")?.takeIf { it > 0L } ?: return null
        return CachedPairingProfile(
            networkKey = networkKey,
            payload = payload,
            hostAddress = normalizedString(json.optionalString("hostAddress")),
            wifiName = normalizedString(json.optionalString("wifiName")),
            hostName = normalizedString(json.optionalString("hostName")),
            cachedAtEpochMs = cachedAtEpochMs,
            expiresAtEpochMs = expiresAtEpochMs,
        )
    }

    private fun sortProfiles(profiles: List<CachedPairingProfile>): List<CachedPairingProfile> {
        return profiles.sortedWith { left, right ->
            val dateComparison = right.cachedAtEpochMs.compareTo(left.cachedAtEpochMs)
            if (dateComparison != 0) {
                dateComparison
            } else {
                left.networkKey.compareTo(right.networkKey, ignoreCase = true)
            }
        }
    }

    private fun normalizedString(value: String?): String? {
        return value?.trim()?.ifBlank { null }
    }

    private fun optionalLong(json: JSONObject, name: String): Long? {
        if (!json.has(name) || json.isNull(name)) {
            return null
        }
        return json.optLong(name)
    }

    private fun addSaturated(left: Long, right: Long): Long {
        if (Long.MAX_VALUE - left < right) {
            return Long.MAX_VALUE
        }
        return left + right
    }

    private data class ParsedProfiles(
        val profiles: List<CachedPairingProfile>,
        val shouldRewrite: Boolean,
    )
}
