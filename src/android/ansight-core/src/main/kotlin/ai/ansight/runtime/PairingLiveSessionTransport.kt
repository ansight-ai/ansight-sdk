package ai.ansight.runtime

import org.json.JSONObject
import org.java_websocket.client.WebSocketClient
import org.java_websocket.handshake.ServerHandshake
import java.net.URI
import java.nio.ByteBuffer
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

internal data class PairingControlRequestResult(
    val operationResult: OperationResult,
    val response: JSONObject?,
)

class PairingLiveSessionTransport {
    private val pendingResponses = ConcurrentHashMap<String, PendingControlResponse>()
    private val lock = Any()
    private var webSocket: WebSocketClient? = null
    private var lastCloseReason: String? = null
    @Volatile var textMessageHandler: ((String) -> Unit)? = null
    @Volatile var binaryMessageHandler: ((ByteArray) -> Unit)? = null

    val isOpen: Boolean
        get() = synchronized(lock) { webSocket?.isOpen == true }

    fun open(url: String, timeoutMilliseconds: Long = 5_000): OperationResult {
        close(notify = false)

        val opened = CountDownLatch(1)
        val failed = AtomicReference<String?>(null)
        val openingSocket = AtomicReference<WebSocketClient?>(null)
        val socket = object : WebSocketClient(URI(url)) {
            override fun onOpen(handshakeData: ServerHandshake?) {
                synchronized(lock) {
                    webSocket = this
                    lastCloseReason = null
                }
                opened.countDown()
            }

            override fun onMessage(message: String) {
                handleIncomingText(message)
            }

            override fun onMessage(bytes: ByteBuffer) {
                val copy = ByteArray(bytes.remaining())
                bytes.slice().get(copy)
                binaryMessageHandler?.invoke(copy)
            }

            override fun onClose(code: Int, reason: String?, remote: Boolean) {
                handleSocketClosed(
                    this,
                    reason?.takeIf { it.isNotBlank() } ?: "WebSocket closed.",
                )
            }

            override fun onError(error: Exception?) {
                val message = error?.message ?: "WebSocket failed."
                failed.compareAndSet(null, message)
                handleSocketClosed(this, message)
                opened.countDown()
            }
        }

        openingSocket.set(socket)
        socket.connect()
        if (!opened.await(timeoutMilliseconds, TimeUnit.MILLISECONDS)) {
            openingSocket.get()?.closeConnection(
                1001,
                "WebSocket endpoint did not become reachable in time.",
            )
            close(reason = "WebSocket endpoint did not become reachable in time.", notify = true)
            return OperationResult.failure("WebSocket endpoint did not become reachable in time.")
        }

        val failure = failed.get()
        if (failure != null) {
            return OperationResult.failure("WebSocket endpoint did not become reachable: $failure")
        }

        return OperationResult.success("WebSocket session opened.")
    }

    fun sendControlRequest(
        action: String,
        payload: JSONObject?,
        timeoutMilliseconds: Long = 15_000,
    ): OperationResult {
        return sendControlRequestWithResponse(
            action,
            payload,
            timeoutMilliseconds,
        ).operationResult
    }

    internal fun sendControlRequestWithResponse(
        action: String,
        payload: JSONObject?,
        timeoutMilliseconds: Long = 15_000,
    ): PairingControlRequestResult {
        val socket = synchronized(lock) { webSocket }
            ?: return PairingControlRequestResult(
                OperationResult.failure("WebSocket session is not open."),
                null,
            )

        val requestId = "client.${UUID.randomUUID().toString().replace("-", "")}"
        val envelope = JSONObject()
            .put("type", "CONTROL_REQ")
            .put("id", requestId)
            .put("action", action)
            .putNullable("payload", payload)
            .put("success", true)

        val pending = PendingControlResponse()
        pendingResponses[requestId] = pending
        if (!sendTextPayload(socket, envelope.toString())) {
            pendingResponses.remove(requestId)
            return PairingControlRequestResult(
                OperationResult.failure("Failed to send $action."),
                null,
            )
        }

        if (!pending.latch.await(timeoutMilliseconds, TimeUnit.MILLISECONDS)) {
            pendingResponses.remove(requestId)
            return PairingControlRequestResult(
                OperationResult.failure("Timed out waiting for $action acknowledgement."),
                null,
            )
        }

        val response = pending.response
            ?: return PairingControlRequestResult(
                OperationResult.failure(pending.error ?: "No acknowledgement payload received for $action."),
                null,
            )
        val operationResult = if (response.optBoolean("success", false)) {
            OperationResult.success(response.optionalString("message") ?: "$action acknowledged.")
        } else {
            OperationResult.failure(response.optionalString("message") ?: "$action failed.")
        }
        return PairingControlRequestResult(operationResult, response)
    }

    fun sendText(text: String): OperationResult {
        val socket = synchronized(lock) { webSocket }
            ?: return OperationResult.failure("WebSocket session is not open.")
        return if (sendTextPayload(socket, text)) {
            OperationResult.success("Payload sent.")
        } else {
            OperationResult.failure("Failed to send WebSocket payload.")
        }
    }

    fun sendData(bytes: ByteArray): OperationResult {
        val socket = synchronized(lock) { webSocket }
            ?: return OperationResult.failure("WebSocket session is not open.")
        return if (sendBinaryPayload(socket, bytes)) {
            OperationResult.success("Binary payload sent.")
        } else {
            OperationResult.failure("Failed to send WebSocket binary payload.")
        }
    }

    fun close(reason: String = "WebSocket session closed.", notify: Boolean = false): OperationResult {
        val socket = synchronized(lock) {
            val current = webSocket
            webSocket = null
            lastCloseReason = reason
            current
        }

        pendingResponses.values.forEach { pending ->
            pending.error = reason
            pending.latch.countDown()
        }
        pendingResponses.clear()
        socket?.close(1000, reason.take(120))
        return if (notify) OperationResult.failure(reason) else OperationResult.success(reason)
    }

    private fun handleSocketClosed(socket: WebSocketClient, reason: String) {
        val shouldNotify = synchronized(lock) {
            if (webSocket !== socket) {
                false
            } else {
                webSocket = null
                lastCloseReason = reason
                true
            }
        }
        if (!shouldNotify) {
            return
        }

        pendingResponses.values.forEach { pending ->
            pending.error = reason
            pending.latch.countDown()
        }
        pendingResponses.clear()
    }

    private fun sendTextPayload(socket: WebSocketClient, text: String): Boolean =
        socket.isOpen && runCatching {
            socket.send(text)
        }.isSuccess

    private fun sendBinaryPayload(socket: WebSocketClient, bytes: ByteArray): Boolean =
        socket.isOpen && runCatching {
            socket.send(bytes)
        }.isSuccess

    private fun handleIncomingText(text: String) {
        val json = try {
            JSONObject(text)
        } catch (_: Exception) {
            return
        }

        if (json.optionalString("type") == "CONTROL_RESP") {
            val replyTo = json.optionalString("replyTo") ?: return
            val pending = pendingResponses.remove(replyTo) ?: return
            pending.response = json
            pending.latch.countDown()
            return
        }

        textMessageHandler?.invoke(text)
    }

    private class PendingControlResponse {
        val latch = CountDownLatch(1)
        @Volatile var response: JSONObject? = null
        @Volatile var error: String? = null
    }
}
