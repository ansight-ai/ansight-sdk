package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class PairingConfigDocumentServiceTest {
    @Test
    fun parseDocumentAcceptsBarePairingConfig() {
        val document = PairingConfigDocumentService.parseDocument(studioConfigJson)

        assertEquals("5ca35358e2ab4555bcfe14ed9aa1a9d0", document.config.configId)
        assertEquals("ai.ansight.ios.native-harness", document.config.appId)
    }

    @Test
    fun verifiesStudioPairingConfigSignature() {
        val document = PairingConfigDocumentService.parseDocument(studioConfigJson)

        assertTrue(PairingConfigDocumentService.verifyPairingConfigSignature(document.config))
    }

    @Test
    fun rejectsTamperedPairingConfigSignature() {
        val document = PairingConfigDocumentService.parseDocument(
            studioConfigJson.replace("Ansight iOS Native Harness", "Tampered Harness"),
        )

        assertFalse(PairingConfigDocumentService.verifyPairingConfigSignature(document.config))
    }

    @Test
    fun validateDocumentRejectsExpiredPairingConfig() {
        val document = PairingConfigDocumentService.parseDocument(studioConfigJson)

        val error = assertThrows(IllegalArgumentException::class.java) {
            PairingConfigDocumentService.validateDocument(document, "ai.ansight.ios.native-harness")
        }
        assertTrue(error.message?.contains("expired", ignoreCase = true) == true)
    }

    private val studioConfigJson = """
        {
          "schema": "ansight.pairing-config.v1",
          "configId": "5ca35358e2ab4555bcfe14ed9aa1a9d0",
          "appId": "ai.ansight.ios.native-harness",
          "appName": "Ansight iOS Native Harness",
          "issuedAt": "2026-06-14T03:05:36.954789+00:00",
          "expiresAt": "2026-06-14T15:05:36.954789+00:00",
          "oneTimeToken": "JUUUaLfYHNJxP6awJ8etzj6YXN-KAGO2KE4PpidF95s",
          "host": {
            "hostPubKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE6r743De/Qz3D8wF19E7GBfhqropvzy/xyli4L6QpGoeLSoM74Fy3cmCYNgce0uk9kmmZdUW/ZKQ5mL/EeaMuQw==",
            "hostPubKeyFingerprint": "41a2469e7f956d0c"
          },
          "challenge": {
            "alg": "ECDSA-P256",
            "challengePubKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE1mV/5MogTFprgVnbEhVrxiQSPy+PqVhzvv8SiGgCv9i4aBySuXBKSg50O/W8fG4xmZXcEy0ILllI4go4oLR8dQ==",
            "requireProofOnFirstPair": true
          },
          "trust": {
            "mode": "pinned-key+token+challenge",
            "requireTokenOnFirstPair": true,
            "allowLanDiscovery": false
          },
          "signature": "xW8rvW+V7EsDH8TDp5ULI8aXabZX4SA0ZNO9VRkCpYaRgvkKmfdI1J/Qtb8tQkN/DBzpAwKlqarD9pQztQ+/dA=="
        }
    """.trimIndent()
}
