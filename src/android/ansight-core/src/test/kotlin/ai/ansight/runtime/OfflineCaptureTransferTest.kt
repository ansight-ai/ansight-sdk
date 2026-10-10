package ai.ansight.runtime

import java.io.ByteArrayOutputStream
import java.net.InetSocketAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import org.java_websocket.WebSocket
import org.java_websocket.handshake.ClientHandshake
import org.java_websocket.server.WebSocketServer
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class OfflineCaptureTransferTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun automaticTransferPurgesOnlyMatchingReceipt() = exercise(OfflineCaptureTransferMode.Automatic, false)
    @Test fun manualTransferWaitsForExplicitRequest() = exercise(OfflineCaptureTransferMode.Manual, false)
    @Test fun mismatchedReceiptRetainsCapture() = exercise(OfflineCaptureTransferMode.Automatic, true)

    private fun exercise(mode: OfflineCaptureTransferMode, badReceipt: Boolean) {
        val server = ReceiptServer(badReceipt)
        server.start()
        assertTrue(server.started.await(5, TimeUnit.SECONDS))
        val transport = PairingLiveSessionTransport()
        val options = OfflineCaptureOptions(enabled = true, transferMode = mode)
        val store = OfflineCaptureStore(temporary.newFolder(), options, "test", "process")
        val capture = NativeOfflineCapture(store, options, temporary.newFolder())
        try {
            assertTrue(transport.open("ws://127.0.0.1:${server.port}").success)
            capture.update(true, null)
            assertTrue(capture.append(JSONObject().put("metrics", org.json.JSONArray().put(JSONObject().put("channel", 1).put("value", 4)))))
            capture.update(true, transport)
            if (mode == OfflineCaptureTransferMode.Manual) {
                store.flush().get(5, TimeUnit.SECONDS)
                assertEquals(0, server.controls.get())
                assertTrue(capture.transfer().success)
            }
            val deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(5)
            while (System.nanoTime() < deadline) {
                if (if (badReceipt) capture.lastError != null else store.sessions().get().isEmpty()) break
                Thread.sleep(10)
            }
            assertNull(server.error)
            assertEquals(if (badReceipt) 1 else 0, store.sessions().get().size)
            if (badReceipt) assertNotNull(capture.lastError)
            assertTrue(server.archive.size() > 0)
        } finally {
            capture.close()
            transport.close()
            server.stop(1000)
        }
    }

    private class ReceiptServer(private val badReceipt: Boolean) : WebSocketServer(InetSocketAddress("127.0.0.1", 0)) {
        val started = CountDownLatch(1)
        val controls = AtomicInteger()
        val archive = ByteArrayOutputStream()
        @Volatile var error: String? = null
        private var sequence = 0
        private var complete = false
        override fun onStart() { started.countDown() }
        override fun onOpen(connection: WebSocket, handshake: ClientHandshake) {}
        override fun onClose(connection: WebSocket, code: Int, reason: String, remote: Boolean) {}
        override fun onError(connection: WebSocket?, exception: Exception) { error = exception.toString() }
        override fun onMessage(connection: WebSocket, message: ByteBuffer) {
            try {
                val frame = message.slice().order(ByteOrder.LITTLE_ENDIAN)
                assertEquals(0x54465341, frame.getInt(0))
                assertEquals(sequence, frame.getInt(40))
                assertEquals(archive.size().toLong(), frame.getLong(44))
                val size = frame.getInt(52)
                assertEquals(size + 56, frame.remaining())
                if (frame.get(5).toInt() == 1) {
                    val payload = ByteArray(size)
                    frame.position(56)
                    frame.get(payload)
                    archive.write(payload)
                    sequence++
                } else { assertEquals(0, size); complete = true }
            } catch (failure: Throwable) { error = failure.toString() }
        }
        override fun onMessage(connection: WebSocket, message: String) {
            controls.incrementAndGet()
            val request = JSONObject(message)
            val response = JSONObject().put("type", "CONTROL_RESP").put("replyTo", request.getString("id")).put("success", true)
            if (request.getString("action") == "offline_capture.commit") {
                val descriptor = request.getJSONObject("payload")
                try {
                    assertTrue(complete)
                    assertEquals(descriptor.getLong("byteCount"), archive.size().toLong())
                    val hash = MessageDigest.getInstance("SHA-256").digest(archive.toByteArray()).joinToString("") { "%02x".format(it) }
                    assertEquals(descriptor.getString("sha256"), hash)
                } catch (failure: Throwable) { error = failure.toString() }
                descriptor.put("durable", true)
                if (badReceipt) descriptor.put("sha256", "0".repeat(64))
                response.put("payload", descriptor)
            }
            connection.send(response.toString())
        }
    }
}
