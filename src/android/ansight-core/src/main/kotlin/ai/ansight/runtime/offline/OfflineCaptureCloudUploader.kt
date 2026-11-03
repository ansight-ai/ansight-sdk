package ai.ansight.runtime

import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.Future
import org.json.JSONObject

/** The caller supplies an upload-only key from secure configuration. It is never
 * persisted in SDK options, capture metadata, or exported evidence. */
class OfflineCaptureUploadConfiguration(val endpoint: URL, val apiKey: String) {
    init {
        val local = endpoint.host == "localhost" || endpoint.host == "127.0.0.1"
        require(endpoint.protocol == "https" || (endpoint.protocol == "http" && local))
        require(endpoint.userInfo == null)
        require(apiKey.startsWith("an_cap_") || apiKey.startsWith("an_run_"))
    }
}

data class OfflineCaptureUploadResult(val sessionIds: List<String>, val acknowledgedChunks: Int, val pendingChunks: Int)

/** One serialized, resumable pass over sealed chunks. Repeat after reconnecting
 * or while server processing is pending. Only hash-confirmed ready chunks retire. */
class OfflineCaptureCloudUploader(
    private val store: OfflineCaptureStore,
    private val configuration: OfflineCaptureUploadConfiguration,
    private val exportDirectory: File,
) : AutoCloseable {
    private val executor = Executors.newSingleThreadExecutor { task ->
        Thread(task, "Ansight Cloud Upload").apply { isDaemon = true }
    }

    fun uploadPending(): Future<OfflineCaptureUploadResult> = executor.submit<OfflineCaptureUploadResult> {
        // sessions() is a queue barrier, including while retention blocks writes.
        val captures = store.sessions().get().filter { it.captureId != null && it.stoppedAtUtc != null }
        val sessions = mutableSetOf<String>()
        var acknowledged = 0
        var pending = 0
        var processingFailure: IOException? = null
        if (!exportDirectory.isDirectory && !exportDirectory.mkdirs()) throw IOException("Cannot create capture export directory.")
        for (capture in captures) {
            if (Thread.currentThread().isInterrupted) throw InterruptedException()
            val lease = store.acquire(capture.sessionId).get()
            val file = File(exportDirectory, "ansight-cloud-${UUID.randomUUID()}.zip")
            var released = false
            try {
                val archive = OfflineCaptureArchive.write(lease, file)
                val begun = request(JSONObject().put("action", "begin").put("sessionId", capture.captureId)
                    .put("appId", capture.appId).put("startedAtUtc", capture.captureStartedAtUtc ?: capture.startedAtUtc))
                val sessionId = begun.getString("sessionId")
                sessions.add(sessionId)
                val sequence = requireNotNull(capture.sequence)
                val reserved = request(JSONObject().put("action", "chunk").put("sessionId", sessionId)
                    .put("sequence", sequence).put("sha256", archive.sha256).put("byteSize", archive.byteCount)
                    .put("startedAtUtc", capture.startedAtUtc).put("endedAtUtc", capture.stoppedAtUtc))
                if (reserved.has("uploadUrl")) {
                    val url = URL(configuration.endpoint, reserved.getString("uploadUrl"))
                    val trustedOrigin = url.host == configuration.endpoint.host
                        && url.protocol == configuration.endpoint.protocol
                        && url.port == configuration.endpoint.port
                    require(trustedOrigin)
                    upload(url, file)
                }
                request(JSONObject().put("action", "commit").put("sessionId", sessionId).put("sequence", sequence))
                if (capture.terminationKind != "sizeLimit" && capture.terminationKind != "chunkLimit") {
                    request(JSONObject().put("action", "finish").put("sessionId", sessionId).put("finalSequence", sequence))
                }
                val status = request(JSONObject().put("action", "status").put("sessionId", sessionId))
                val chunks = status.getJSONArray("chunks")
                val receipt = (0 until chunks.length()).map { chunks.getJSONObject(it) }
                    .firstOrNull { it.getInt("sequence") == sequence }
                if (receipt?.optString("status") == "failed") {
                    processingFailure = IOException(receipt.optString("failureMessage", "Chunk processing failed."))
                }
                if (receipt?.optString("status") == "ready" && receipt.optString("sha256") == archive.sha256) {
                    store.acknowledge(lease, archive.sha256, archive.sha256).get()
                    acknowledged++
                } else {
                    store.release(lease).get()
                    pending++
                }
                released = true
            } finally {
                if (!released) runCatching { store.release(lease).get() }
                file.delete()
            }
        }
        processingFailure?.let { throw it }
        OfflineCaptureUploadResult(sessions.sorted(), acknowledged, pending)
    }

    private fun connection(url: URL, method: String): HttpURLConnection =
        (url.openConnection() as HttpURLConnection).apply {
            requestMethod = method
            instanceFollowRedirects = false
            connectTimeout = 30_000
            readTimeout = 60_000
            doOutput = true
        }

    private fun upload(url: URL, file: File) {
        val connection = connection(url, "PUT")
        try {
            connection.setRequestProperty("Content-Type", "application/zip")
            connection.setFixedLengthStreamingMode(file.length().toInt())
            connection.outputStream.use { output -> file.inputStream().use { it.copyTo(output, 64 * 1024) } }
            val status = connection.responseCode
            val uploadRejected = status !in 200..299
                && status != 400
                && status != 409
            if (uploadRejected) throw IOException("Chunk upload failed with HTTP $status.")
        } finally {
            connection.disconnect()
        }
    }

    private fun request(body: JSONObject): JSONObject {
        val connection = connection(configuration.endpoint, "POST")
        try {
            val bytes = body.toString().toByteArray(Charsets.UTF_8)
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Authorization", "Bearer ${configuration.apiKey}")
            connection.setFixedLengthStreamingMode(bytes.size)
            connection.outputStream.use { it.write(bytes) }
            val status = connection.responseCode
            if (status !in 200..299) throw IOException("Capture API request failed with HTTP $status.")
            val response = connection.inputStream.use { input ->
                val output = java.io.ByteArrayOutputStream()
                val buffer = ByteArray(8192)
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    if (output.size() + count > 2 * 1024 * 1024) throw IOException("Capture API response exceeds its budget.")
                    output.write(buffer, 0, count)
                }
                output.toString("UTF-8")
            }
            return JSONObject(response)
        } finally {
            connection.disconnect()
        }
    }

    override fun close() { executor.shutdownNow() }
}
