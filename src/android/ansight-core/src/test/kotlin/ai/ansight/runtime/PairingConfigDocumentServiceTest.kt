package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.time.Instant
import java.util.Base64
import java.util.zip.GZIPOutputStream

class PairingConfigDocumentServiceTest {
    @Test
    fun parseDocumentAcceptsBareEnrollmentInvite() {
        val document = PairingConfigDocumentService.parseDocument(inviteJson())

        assertEquals("invite-1", document.config.configId)
        assertEquals("ai.ansight.android.test", document.config.appId)
        assertEquals(accessToken, document.config.enrollment.accessToken)
    }

    @Test
    fun parseDocumentAcceptsCompactEnrollmentCode() {
        val document = PairingConfigDocumentService.parseDocument(compactCode(documentJson()))

        assertEquals("invite-1", document.config.configId)
        assertEquals(listOf("192.168.1.24", "fd00::24"), document.discoveryHint?.hostAddresses)
        assertEquals(45_200, document.discoveryHint?.discoveryPort)
        assertEquals("studio-qr", document.discoveryHint?.source)
    }

    @Test
    fun parseDocumentRejectsMalformedCompactInvite() {
        val error = assertThrows(IllegalArgumentException::class.java) {
            PairingConfigDocumentService.parseDocument("ans2:not-an-invite")
        }

        assertTrue(error.message?.contains("parse", ignoreCase = true) == true)
    }

    @Test
    fun validateDocumentAllowsReconnectAfterQrInviteExpiry() {
        val document = PairingConfigDocumentService.parseDocument(
            inviteJson(inviteExpiry = "2020-01-01T00:00:00Z"),
        )

        PairingConfigDocumentService.validateDocument(document, "ai.ansight.android.test")
    }

    @Test
    fun validateDocumentRejectsExpiredDeviceRegistration() {
        val document = PairingConfigDocumentService.parseDocument(
            inviteJson(registrationExpiry = "2020-01-01T00:00:00Z"),
        )

        val error = assertThrows(IllegalArgumentException::class.java) {
            PairingConfigDocumentService.validateDocument(document, "ai.ansight.android.test")
        }
        assertTrue(error.message?.contains("registration expired", ignoreCase = true) == true)
    }

    @Test
    fun parseConfigInstantAcceptsOffsetTimestamps() {
        val parsed = PairingConfigDocumentService.parseConfigInstant("2026-06-15T10:49:15.800804+10:00")

        assertEquals(Instant.parse("2026-06-15T00:49:15.800804Z"), parsed)
    }

    private fun documentJson(): String = JSONObject()
        .put("schema", PairingConfigDocumentService.ConfigDocumentSchemaName)
        .put("invite", JSONObject(inviteJson()))
        .put(
            "discovery",
            JSONObject()
                .put("schema", "ansight.discovery-hint.v1")
                .put("source", "studio-qr")
                .put("hostAddresses", JSONArray(listOf("192.168.1.24", "fd00::24")))
                .put("discoveryPort", 45_200)
                .put("hostName", "Host Node")
                .put("wifiName", "Office Wifi"),
        )
        .toString()

    private fun compactCode(documentJson: String): String {
        val output = ByteArrayOutputStream()
        GZIPOutputStream(output).use { gzip ->
            gzip.write(documentJson.toByteArray(Charsets.UTF_8))
        }
        val encoded = Base64.getUrlEncoder().withoutPadding().encodeToString(output.toByteArray())
        return "${PairingConfigCodeGenerator.FormatPrefix}:$encoded"
    }

    private fun inviteJson(
        inviteExpiry: String = "2099-01-01T00:00:00Z",
        registrationExpiry: String = "2099-02-01T00:00:00Z",
    ): String = JSONObject()
        .put("schema", PairingConfig.SchemaName)
        .put("inviteId", "invite-1")
        .put("appId", "ai.ansight.android.test")
        .put("appName", "Android Test")
        .put("issuedAt", "2026-06-14T03:05:36Z")
        .put("expiresAt", inviteExpiry)
        .put("minProtocolVersion", 2)
        .put("allowedTransports", JSONArray().put("ws"))
        .put(
            "host",
            JSONObject()
                .put("hostId", "host-1")
                .put("hostName", "Studio")
                .put("discoveryPort", 45_123),
        )
        .put(
            "enrollment",
            JSONObject()
                .put("accessToken", accessToken)
                .put("expiresAt", inviteExpiry)
                .put("grantExpiresAt", registrationExpiry)
                .put("maxUses", 1)
                .put("maxScopes", JSONArray().put("Read"))
                .put("allowCritical", false),
        )
        .toString()

    private companion object {
        const val accessToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    }
}
