package ai.ansight.runtime

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import kotlin.concurrent.thread

class PairingSessionConnectorTest {
    @Test
    fun connectTriesNextDiscoveryAddressWhenFirstFails() {
        val loopback = InetAddress.getByName("127.0.0.1")
        val listener = DatagramSocket(0, loopback)
        val responder = thread {
            val buffer = ByteArray(16 * 1024)
            val request = DatagramPacket(buffer, buffer.size)
            listener.receive(request)

            val response = JSONObject()
                .put("type", "CONNECT_RESP")
                .put("ver", 1)
                .put("accepted", false)
                .put("reason", "pairing-required")
                .put("reasonMessage", "Need WebSocket handoff")
                .put("hostId", "host-1")
                .put("hostName", "Host")
                .put("message", "Rejected")
                .toString()
                .toByteArray(Charsets.UTF_8)
            listener.send(DatagramPacket(response, response.size, request.address, request.port))
        }

        try {
            val result = PairingSessionConnector().connect(
                document = ParsedPairingDocument(
                    config = pairingConfig(listener.localPort),
                    discoveryHint = PairingDiscoveryHint(
                        hostAddresses = listOf("bad host", "127.0.0.1"),
                        discoveryPort = listener.localPort,
                    ),
                ),
                clientName = "Unit Test App",
            )

            assertFalse(result.success)
            assertEquals("127.0.0.1", result.hostAddress)
            assertEquals("Need WebSocket handoff", result.message)
        } finally {
            listener.close()
            responder.join(1_000)
        }
    }

    @Test
    fun discoveryHintHostAddressCandidatesNormalizeDirectValues() {
        val hint = PairingDiscoveryHint(
            hostAddresses = listOf(" ", " 10.0.0.8 ", "10.0.0.8", "192.168.1.20"),
        )

        assertEquals(listOf("10.0.0.8", "192.168.1.20"), hint.hostAddressCandidates)
        assertEquals("10.0.0.8", hint.hostAddress)
    }

    private fun pairingConfig(discoveryPort: Int): PairingConfig = PairingConfig(
        schema = PairingConfig.SchemaName,
        configId = "cfg-multi-address",
        appId = "ai.ansight.test",
        appName = "Ansight Test",
        issuedAt = "2026-06-19T00:00:00Z",
        expiresAt = "2026-06-20T00:00:00Z",
        oneTimeToken = "token",
        host = PairingHost(
            hostId = "host-1",
            hostName = "Host",
            discoveryPort = discoveryPort,
            hostPubKey = "pub",
            hostPubKeyFingerprint = "fingerprint",
        ),
        challenge = PairingChallenge(
            alg = "ECDSA-P256",
            challengePubKey = "challenge",
            requireProofOnFirstPair = true,
        ),
        signature = "signature",
    )
}
