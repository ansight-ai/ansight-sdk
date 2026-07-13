package ai.ansight.runtime

import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString
import org.json.JSONObject
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

class PairingLiveSessionTransport {
    private val maximumAuthenticationMessageBytes = 64 * 1024
    private val defaultClient = OkHttpClient.Builder()
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .build()
    private val pendingResponses = ConcurrentHashMap<String, PendingControlResponse>()
    private val pendingTextMessages = LinkedBlockingQueue<String>()
    private val lock = Any()
    private var webSocket: WebSocket? = null
    private var lastCloseReason: String? = null
    @Volatile var textMessageHandler: ((String) -> Unit)? = null
    @Volatile var binaryMessageHandler: ((ByteArray) -> Unit)? = null

    val isOpen: Boolean
        get() = synchronized(lock) { webSocket != null }

    fun open(url: String, timeoutMilliseconds: Long = 5_000): OperationResult {
        return open(url, defaultClient, timeoutMilliseconds)
    }

    internal fun openPinnedWss(url: String, tlsSpkiSha256: String, timeoutMilliseconds: Long = 5_000): OperationResult {
        if (!url.startsWith("wss://", ignoreCase = true)) {
            return OperationResult.failure("Protocol v2 requires a wss:// endpoint.")
        }
        return runCatching {
            open(url, PairingV2Tls.createClient(tlsSpkiSha256), timeoutMilliseconds)
        }.getOrElse { error ->
            OperationResult.failure(PairingRedaction.redact("WSS endpoint did not become reachable: ${error.message}"))
        }
    }

    internal fun awaitTextMessage(timeoutMilliseconds: Long): String? =
        pendingTextMessages.poll(timeoutMilliseconds, TimeUnit.MILLISECONDS)

    private fun open(url: String, client: OkHttpClient, timeoutMilliseconds: Long): OperationResult {
        close(notify = false)
        pendingTextMessages.clear()

        val opened = CountDownLatch(1)
        val failed = AtomicReference<String?>(null)
        val request = Request.Builder().url(url).build()
        val listener = object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                synchronized(lock) {
                    this@PairingLiveSessionTransport.webSocket = webSocket
                    lastCloseReason = null
                }
                opened.countDown()
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                if (textMessageHandler == null) {
                    if (text.toByteArray(Charsets.UTF_8).size > maximumAuthenticationMessageBytes) {
                        pendingTextMessages.offer(
                            JSONObject()
                                .put("type", PairingV2Constants.AuthErrorType)
                                .put("ver", 2)
                                .put("code", "authentication_message_too_large")
                                .put("message", "Protocol v2 authentication message exceeded the size limit.")
                                .put("retryable", false)
                                .toString(),
                        )
                        close(reason = "Protocol v2 authentication message exceeded the size limit.", notify = true)
                    } else {
                        pendingTextMessages.offer(text)
                    }
                } else {
                    handleIncomingText(text)
                }
            }

            override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
                binaryMessageHandler?.invoke(bytes.toByteArray())
            }

            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                close(reason = reason.ifBlank { "WebSocket closing." }, notify = true)
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                close(reason = reason.ifBlank { "WebSocket closed." }, notify = true)
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                failed.set(PairingRedaction.redact(t.message ?: "WebSocket failed."))
                close(reason = failed.get() ?: "WebSocket failed.", notify = true)
                opened.countDown()
            }
        }

        client.newWebSocket(request, listener)
        if (!opened.await(timeoutMilliseconds, TimeUnit.MILLISECONDS)) {
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
        val socket = synchronized(lock) { webSocket }
            ?: return OperationResult.failure("WebSocket session is not open.")

        val requestId = "client.${UUID.randomUUID().toString().replace("-", "")}"
        val envelope = JSONObject()
            .put("type", "CONTROL_REQ")
            .put("id", requestId)
            .put("action", action)
            .putNullable("payload", payload)
            .put("success", true)

        val pending = PendingControlResponse()
        pendingResponses[requestId] = pending
        if (!socket.send(envelope.toString())) {
            pendingResponses.remove(requestId)
            return OperationResult.failure("Failed to send $action.")
        }

        if (!pending.latch.await(timeoutMilliseconds, TimeUnit.MILLISECONDS)) {
            pendingResponses.remove(requestId)
            return OperationResult.failure("Timed out waiting for $action acknowledgement.")
        }

        val response = pending.response
            ?: return OperationResult.failure(pending.error ?: "No acknowledgement payload received for $action.")
        return if (response.optBoolean("success", false)) {
            OperationResult.success(response.optionalString("message") ?: "$action acknowledged.")
        } else {
            OperationResult.failure(response.optionalString("message") ?: "$action failed.")
        }
    }

    fun sendText(text: String): OperationResult {
        val socket = synchronized(lock) { webSocket }
            ?: return OperationResult.failure("WebSocket session is not open.")
        return if (socket.send(text)) {
            OperationResult.success("Payload sent.")
        } else {
            OperationResult.failure("Failed to send WebSocket payload.")
        }
    }

    fun sendData(bytes: ByteArray): OperationResult {
        val socket = synchronized(lock) { webSocket }
            ?: return OperationResult.failure("WebSocket session is not open.")
        return if (socket.send(OkioCompat.byteStringOf(bytes))) {
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
