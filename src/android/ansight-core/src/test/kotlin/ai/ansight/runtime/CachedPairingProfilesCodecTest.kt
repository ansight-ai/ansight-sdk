package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class CachedPairingProfilesCodecTest {
    @Test
    fun upsertRemembersMultipleProfilesNewestFirstAndReplacesMatchingWifi() {
        val officePayload = configDocument("cfg-office-1", "Office WiFi", "Office Host")
        val labPayload = configDocument("cfg-lab", "Lab WiFi", "Lab Host")
        val updatedOfficePayload = configDocument("cfg-office-2", "Office WiFi", "Office Host")

        var profiles = CachedPairingProfilesCodec.upsert(
            existingProfiles = emptyList(),
            payload = officePayload,
            hostAddress = "10.0.0.10",
            document = PairingConfigDocumentService.parseDocument(officePayload),
            nowEpochMs = 1_000,
            retentionMillis = 60_000,
        )
        profiles = CachedPairingProfilesCodec.upsert(
            existingProfiles = profiles,
            payload = labPayload,
            hostAddress = "10.0.0.20",
            document = PairingConfigDocumentService.parseDocument(labPayload),
            nowEpochMs = 2_000,
            retentionMillis = 60_000,
        )
        profiles = CachedPairingProfilesCodec.upsert(
            existingProfiles = profiles,
            payload = updatedOfficePayload,
            hostAddress = "10.0.0.12",
            document = PairingConfigDocumentService.parseDocument(updatedOfficePayload),
            nowEpochMs = 3_000,
            retentionMillis = 60_000,
        )

        assertEquals(listOf("wifi:Office WiFi", "wifi:Lab WiFi"), profiles.map { it.networkKey })
        assertEquals(updatedOfficePayload, profiles[0].payload)
        assertEquals("10.0.0.12", profiles[0].hostAddress)
        assertEquals(63_000L, profiles[0].expiresAtEpochMs)
    }

    @Test
    fun loadMigratesLegacySingleProfile() {
        val payload = configDocument("cfg-office", "Office WiFi", "Office Host")

        val result = CachedPairingProfilesCodec.load(
            profilesJson = null,
            legacyPayload = payload,
            legacyHostAddress = "10.0.0.10",
            legacyCachedAtEpochMs = 1_000,
            legacyExpiresAtEpochMs = 61_000,
            nowEpochMs = 2_000,
        )

        assertTrue(result.shouldRewrite)
        assertEquals(1, result.profiles.size)
        assertEquals("wifi:Office WiFi", result.profiles[0].networkKey)
        assertEquals("10.0.0.10", result.profiles[0].hostAddress)
    }

    @Test
    fun loadDropsExpiredProfilesAndRequestsRewrite() {
        val expiredPayload = configDocument("cfg-expired", "Expired WiFi", "Expired Host")
        val validPayload = configDocument("cfg-valid", "Valid WiFi", "Valid Host")
        val profilesJson = CachedPairingProfilesCodec.serialize(
            listOf(
                CachedPairingProfile(
                    networkKey = "wifi:Expired WiFi",
                    payload = expiredPayload,
                    hostAddress = "10.0.0.1",
                    wifiName = "Expired WiFi",
                    hostName = "Expired Host",
                    cachedAtEpochMs = 1_000,
                    expiresAtEpochMs = 1_500,
                ),
                CachedPairingProfile(
                    networkKey = "wifi:Valid WiFi",
                    payload = validPayload,
                    hostAddress = "10.0.0.2",
                    wifiName = "Valid WiFi",
                    hostName = "Valid Host",
                    cachedAtEpochMs = 2_000,
                    expiresAtEpochMs = 62_000,
                ),
            ),
        )

        assertNotNull(profilesJson)
        val result = CachedPairingProfilesCodec.load(
            profilesJson = profilesJson,
            legacyPayload = null,
            legacyHostAddress = null,
            legacyCachedAtEpochMs = 0,
            legacyExpiresAtEpochMs = 0,
            nowEpochMs = 2_000,
        )

        assertTrue(result.shouldRewrite)
        assertEquals(listOf("wifi:Valid WiFi"), result.profiles.map { it.networkKey })
    }

    @Test
    fun loadKeepsCurrentSerializedProfilesWithoutRewrite() {
        val payload = configDocument("cfg-current", "Current WiFi", "Current Host")
        val profilesJson = CachedPairingProfilesCodec.serialize(
            listOf(
                CachedPairingProfile(
                    networkKey = "wifi:Current WiFi",
                    payload = payload,
                    hostAddress = "10.0.0.3",
                    wifiName = "Current WiFi",
                    hostName = "Current Host",
                    cachedAtEpochMs = 1_000,
                    expiresAtEpochMs = 61_000,
                ),
            ),
        )

        assertNotNull(profilesJson)
        val result = CachedPairingProfilesCodec.load(
            profilesJson = profilesJson,
            legacyPayload = null,
            legacyHostAddress = null,
            legacyCachedAtEpochMs = 0,
            legacyExpiresAtEpochMs = 0,
            nowEpochMs = 2_000,
        )

        assertFalse(result.shouldRewrite)
        assertEquals("wifi:Current WiFi", result.profiles.single().networkKey)
    }

    private fun configDocument(configId: String, wifiName: String, hostName: String): String {
        val discovery = JSONObject()
            .put("schema", "ansight.discovery-hint.v1")
            .put("source", "test")
            .put("hostAddresses", JSONArray().put("192.168.1.24"))
            .put("discoveryPort", 45_200)
            .put("hostName", hostName)
            .put("wifiName", wifiName)

        return JSONObject()
            .put("schema", PairingConfigDocumentService.ConfigDocumentSchemaName)
            .put(
                "config",
                JSONObject()
                    .put("schema", PairingConfig.SchemaName)
                    .put("configId", configId)
                    .put("appId", "ai.ansight.android.test")
                    .put("appName", "Android Test")
                    .put("issuedAt", "2026-06-15T10:49:15.800804+10:00")
                    .put("expiresAt", "2026-06-15T11:49:15.800804+10:00")
                    .put("oneTimeToken", "token")
                    .put(
                        "host",
                        JSONObject()
                            .put("hostName", hostName)
                            .put("discoveryPort", 45_200)
                            .put("hostPubKey", "host-pub-key")
                            .put("hostPubKeyFingerprint", "host-fingerprint"),
                    )
                    .put(
                        "challenge",
                        JSONObject()
                            .put("alg", "X25519")
                            .put("challengePubKey", "challenge-pub-key")
                            .put("requireProofOnFirstPair", false),
                    )
                    .put("signature", "signature"),
            )
            .put("discovery", discovery)
            .toString()
    }
}
