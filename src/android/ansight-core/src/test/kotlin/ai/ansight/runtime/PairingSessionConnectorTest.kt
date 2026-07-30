package ai.ansight.runtime

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import kotlin.concurrent.thread

class PairingSessionConnectorTest {
    @Test
    fun connectRejectsCellularWhenCellularConnectionsAreDisabled() {
        val result = connector(PairingNetworkPreflightStatus.Cellular).connect(
            document = pairingDocument(45_123),
            clientName = "Unit Test App",
        )

        assertFalse(result.success)
        assertEquals(PairingFailureCodes.WifiRequired, result.failureCode)
        assertTrue(result.message.contains("Cellular"))
    }

    @Test
    fun connectSendsCurrentEnrollmentRequestWhenCellularIsEnabled() {
        val server = EnrollmentResponder()
        try {
            val result = connector(PairingNetworkPreflightStatus.Cellular).connect(
                document = pairingDocument(server.port),
                clientName = "Unit Test App",
                options = PairingConnectionOptions(allowCellularConnections = true),
            )

            assertFalse(result.success)
            assertEquals("127.0.0.1", result.hostAddress)
            assertNotEquals(PairingFailureCodes.WifiRequired, result.failureCode)
            assertEquals("ENROLLMENT_CONNECT", server.request?.getString("type"))
            assertEquals(2, server.request?.getInt("ver"))
            assertEquals("invite-multi-address", server.request?.getString("inviteId"))
            assertEquals("Unit Test App", server.request?.getString("deviceName"))
            assertTrue(server.request?.getString("deviceId")?.startsWith("android.") == true)
        } finally {
            server.close()
        }
    }

    @Test
    fun connectUsesSimulatorLocalHostWhenDiscoveryAddressIsMissing() {
        val server = EnrollmentResponder()
        try {
            val result = PairingSessionConnector(
                simulatorLocalHostAddressProvider = { "127.0.0.1" },
                networkStatusProvider = { PairingNetworkPreflightStatus.Unknown },
            ).connect(
                document = ParsedPairingDocument(
                    config = pairingConfig(server.port),
                    discoveryHint = PairingDiscoveryHint(discoveryPort = server.port),
                ),
                clientName = "Unit Test App",
            )

            assertFalse(result.success)
            assertEquals("127.0.0.1", result.hostAddress)
            assertEquals("Registration required", result.message)
        } finally {
            server.close()
        }
    }

    @Test
    fun connectTriesNextDiscoveryAddressWhenFirstFails() {
        val server = EnrollmentResponder()
        try {
            val result = PairingSessionConnector().connect(
                document = ParsedPairingDocument(
                    config = pairingConfig(server.port),
                    discoveryHint = PairingDiscoveryHint(
                        hostAddresses = listOf("bad host", "127.0.0.1"),
                        discoveryPort = server.port,
                    ),
                ),
                clientName = "Unit Test App",
            )

            assertFalse(result.success)
            assertEquals("127.0.0.1", result.hostAddress)
            assertEquals("Registration required", result.message)
        } finally {
            server.close()
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

    @Test
    fun pairingHostAddressCandidatesPreferSimulatorLocalHostUnlessOverrideIsProvided() {
        val hint = PairingDiscoveryHint(hostAddresses = listOf("192.168.1.20", "127.0.0.1"))

        assertEquals(
            listOf("127.0.0.1", "192.168.1.20"),
            PairingHostAddressCandidates.resolve(hint, null, "127.0.0.1"),
        )
        assertEquals(
            listOf("10.0.0.20"),
            PairingHostAddressCandidates.resolve(hint, "10.0.0.20", "127.0.0.1"),
        )
    }

    private fun connector(status: PairingNetworkPreflightStatus) = PairingSessionConnector(
        simulatorLocalHostAddressProvider = { null },
        networkStatusProvider = { status },
    )

    private fun pairingDocument(port: Int) = ParsedPairingDocument(
        config = pairingConfig(port),
        discoveryHint = PairingDiscoveryHint(
            hostAddresses = listOf("127.0.0.1"),
            discoveryPort = port,
        ),
    )

    private fun pairingConfig(discoveryPort: Int): PairingConfig = PairingConfig(
        schema = PairingConfig.SchemaName,
        configId = "invite-multi-address",
        appId = "ai.ansight.test",
        appName = "Ansight Test",
        issuedAt = "2026-06-19T00:00:00Z",
        expiresAt = "2099-06-20T00:00:00Z",
        host = PairingHost(
            hostId = "host-1",
            hostName = "Host",
            discoveryPort = discoveryPort,
        ),
        enrollment = PairingEnrollment(
            accessToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            expiresAt = "2099-06-20T00:00:00Z",
            grantExpiresAt = "2099-07-20T00:00:00Z",
            maxUses = 1,
            maxScopes = listOf("Read"),
            allowCritical = false,
        ),
    )

    private class EnrollmentResponder {
        private val listener = DatagramSocket(0, InetAddress.getByName("127.0.0.1"))
        private val responder = thread {
            val buffer = ByteArray(16 * 1024)
            val packet = DatagramPacket(buffer, buffer.size)
            listener.receive(packet)
            val requestJson = JSONObject(String(packet.data, packet.offset, packet.length, Charsets.UTF_8))
            request = requestJson
            val response = JSONObject()
                .put("type", "ENROLLMENT_RESULT")
                .put("ver", 2)
                .put("requestId", requestJson.getString("requestId"))
                .put("accepted", false)
                .put("reason", "EnrollmentRequired")
                .put("reasonMessage", "Registration required")
                .put("hostId", "host-1")
                .put("hostName", "Host")
                .put("message", "Rejected")
                .toString()
                .toByteArray(Charsets.UTF_8)
            listener.send(DatagramPacket(response, response.size, packet.address, packet.port))
        }

        val port: Int
            get() = listener.localPort

        @Volatile
        var request: JSONObject? = null
            private set

        fun close() {
            listener.close()
            responder.join(1_000)
        }
    }
}
