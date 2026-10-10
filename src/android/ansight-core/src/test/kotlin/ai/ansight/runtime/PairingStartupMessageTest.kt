package ai.ansight.runtime

import java.net.InetSocketAddress
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import org.java_websocket.WebSocket
import org.java_websocket.handshake.ClientHandshake
import org.java_websocket.server.WebSocketServer
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class PairingStartupMessageTest {
    @Test fun startupRequestsWaitForHandlerAndPreserveOrder() {
        withServer(2) { server, transport ->
            // A control response is a wire-order barrier: both earlier tool
            // queries have arrived, even though no text handler exists yet.
            assertTrue(transport.sendControlRequest("barrier", null).success)
            val received = mutableListOf<Int>()
            transport.textMessageHandler = { received.add(JSONObject(it).getInt("sequence")) }
            assertEquals(listOf(0, 1), received)
            assertTrue(transport.isOpen)
            assertNull(server.error)
        }
    }

    @Test fun installedHandlerReceivesLargeMessagesWithoutStartupBuffering() {
        val server = StartupServer(1, "x".repeat(1_048_577))
        server.start()
        assertTrue(server.started.await(5, TimeUnit.SECONDS))
        val transport = PairingLiveSessionTransport()
        val received = CountDownLatch(1)
        transport.textMessageHandler = { if (JSONObject(it).getString("data").length == 1_048_577) received.countDown() }
        try {
            assertTrue(transport.open("ws://127.0.0.1:${server.port}").success)
            assertTrue(received.await(5, TimeUnit.SECONDS))
            assertTrue(transport.isOpen)
        } finally { transport.close(); server.stop(1000) }
    }

    @Test fun closingDiscardsUndeliveredStartupRequests() {
        withServer(2) { _, transport ->
            assertTrue(transport.sendControlRequest("barrier", null).success)
            transport.close()
            val received = mutableListOf<String>()
            transport.textMessageHandler = { received.add(it) }
            assertTrue(received.isEmpty())
        }
    }

    @Test fun startupQueueIsBounded() {
        withServer(40) { server, transport ->
            assertTrue(server.closed.await(5, TimeUnit.SECONDS))
            assertFalse(transport.isOpen)
            val received = mutableListOf<String>()
            transport.textMessageHandler = { received.add(it) }
            assertTrue(received.isEmpty())
        }
    }

    private fun withServer(count: Int, check: (StartupServer, PairingLiveSessionTransport) -> Unit) {
        val server = StartupServer(count)
        server.start()
        assertTrue(server.started.await(5, TimeUnit.SECONDS))
        val transport = PairingLiveSessionTransport()
        try {
            assertTrue(transport.open("ws://127.0.0.1:${server.port}").success)
            check(server, transport)
        } finally {
            transport.close()
            server.stop(1000)
        }
    }

    private class StartupServer(private val count: Int, private val data: String = "") : WebSocketServer(InetSocketAddress("127.0.0.1", 0)) {
        val started = CountDownLatch(1)
        val closed = CountDownLatch(1)
        @Volatile var error: Exception? = null
        override fun onStart() { started.countDown() }
        override fun onOpen(connection: WebSocket, handshake: ClientHandshake) {
            repeat(count) { connection.send(JSONObject().put("type", "tool.query").put("sequence", it).put("data", data).toString()) }
        }
        override fun onMessage(connection: WebSocket, message: String) {
            val request = JSONObject(message)
            connection.send(JSONObject().put("type", "CONTROL_RESP").put("replyTo", request.getString("id")).put("success", true).toString())
        }
        override fun onClose(connection: WebSocket, code: Int, reason: String, remote: Boolean) { closed.countDown() }
        override fun onError(connection: WebSocket?, exception: Exception) { error = exception }
    }
}
