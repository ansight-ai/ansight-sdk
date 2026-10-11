package ai.ansight.runtime

import java.io.File
import java.io.IOException
import java.util.concurrent.Executors
import org.json.JSONObject

internal class NativeOfflineCapture(val store: OfflineCaptureStore, private val options: OfflineCaptureOptions,
                                    private val temporaryDirectory: File) : AutoCloseable {
    private val exportDirectory = File(File(temporaryDirectory, "AnsightOfflineExports"), store.exportIdentifier)
    private val lock = Any()
    private var transport: PairingLiveSessionTransport? = null
    private var active = false
    private var generation = 0
    private var transferring = false
    private var transferError: String? = null
    private val worker = Executors.newSingleThreadExecutor { Thread(it, "Ansight Offline Transfer").apply { isDaemon = true } }
    val lastError: String? get() = store.lastError ?: synchronized(lock) { transferError }

    fun update(active: Boolean, transport: PairingLiveSessionTransport?) {
        val shouldTransfer = synchronized(lock) {
            val reconnected = transport != null && (transport !== this.transport || (active && !this.active))
            if (transport !== this.transport || active != this.active) generation++
            this.transport = transport
            this.active = active
            store.setRecording(active && transport == null)
            active && reconnected && options.transferMode == OfflineCaptureTransferMode.Automatic
        }
        if (shouldTransfer) transfer()
    }

    fun append(record: JSONObject): Boolean = store.isRecording && store.append(record.toString().toByteArray(Charsets.UTF_8))

    fun transfer(): OperationResult {
        synchronized(lock) {
            val connection = transport
            if (!active || connection == null || transferring) return OperationResult.failure("Offline capture transfer requires an active host connection and no transfer in progress.")
            transferring = true
            transferError = null
            val token = generation
            worker.execute {
                try {
                    if (!exportDirectory.isDirectory && !exportDirectory.mkdirs()) throw IOException("Could not create offline export scratch directory.")
                    exportDirectory.listFiles()?.filter { it.extension == "zip" || it.extension == "part" }?.forEach { it.delete() }
                    store.flush().get()
                    for (session in store.sessions().get().filter { it.stoppedAtUtc != null }) {
                        requireConnection(token, connection)
                        transfer(session.sessionId, token, connection)
                    }
                } catch (error: Exception) {
                    synchronized(lock) { transferError = error.message ?: error.javaClass.simpleName }
                } finally {
                    val retry = synchronized(lock) {
                        transferring = false
                        generation != token && active && transport != null && options.transferMode == OfflineCaptureTransferMode.Automatic
                    }
                    if (retry) transfer()
                }
            }
        }
        return OperationResult.success("Offline capture transfer started.")
    }

    private fun requireConnection(token: Int, connection: PairingLiveSessionTransport) {
        check(synchronized(lock) { active && generation == token && transport === connection } && connection.isOpen) {
            "Host connection changed during offline capture transfer."
        }
    }

    private fun transfer(sessionId: String, generation: Int, transport: PairingLiveSessionTransport) {
        val lease = store.acquire(sessionId).get()
        val path = File(exportDirectory, "ansight-offline-${java.util.UUID.randomUUID()}.zip")
        try {
            val archive = OfflineCaptureArchive.write(lease, path)
            val transferId = PairingFileTransferWireProtocol.newTransferId()
            val descriptor = JSONObject().put("schema", "ansight.offline-transfer.v1").put("sessionId", sessionId)
                .put("transferId", transferId).put("byteCount", archive.byteCount).put("sha256", archive.sha256)
            requireConnection(generation, transport)
            val begin = transport.sendControlRequest("offline_capture.begin", descriptor)
            if (!begin.success) throw IOException(begin.message)
            var sequence = 0
            var offset = 0L
            path.inputStream().use { input ->
                val buffer = ByteArray(64 * 1024)
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    requireConnection(generation, transport)
                    val result = transport.sendFileTransferFrame(PairingFileTransferWireProtocol.createFrame(transferId,
                        FileTransferFrameType.Chunk, sequence++, offset, buffer.copyOf(count)))
                    if (!result.success) throw IOException(result.message)
                    offset += count
                }
            }
            requireConnection(generation, transport)
            val complete = transport.sendFileTransferFrame(PairingFileTransferWireProtocol.createFrame(transferId,
                FileTransferFrameType.Complete, sequence, offset, ByteArray(0)))
            if (!complete.success) throw IOException(complete.message)
            val commit = transport.sendControlRequestWithResponse("offline_capture.commit", descriptor, 120_000)
            val receipt = commit.response?.optJSONObject("payload")
            check(commit.operationResult.success && receipt?.optBoolean("durable") == true &&
                receipt.optString("sessionId") == sessionId && receipt.optString("sha256") == archive.sha256) {
                "Offline capture receipt does not match the exported archive."
            }
            store.acknowledge(lease, archive.sha256, archive.sha256).get()
        } catch (error: Exception) {
            runCatching { store.release(lease).get() }
            throw error
        } finally { path.delete() }
    }

    override fun close() {
        update(false, null)
        // Drain pending transfer work before closing its store.
        worker.execute { store.close() }
        worker.shutdown()
    }
}
