package ai.ansight.runtime

import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.UUID
import java.util.concurrent.Future
import java.util.concurrent.FutureTask
import java.util.concurrent.Executors
import org.json.JSONObject

/** Serial native disk ownership. Producer admission is byte-bounded and lifecycle
 * barriers never share its drop policy. Blocking file work runs on the IO worker. */
class OfflineCaptureStore(
    root: File,
    captureOptions: OfflineCaptureOptions,
    private val appId: String,
    private val processSessionId: String,
    private val sessionHeader: ByteArray? = null,
    private var customProperties: ByteArray? = null,
    private val onSessionStarted: ((String, String) -> Unit)? = null,
    private val onSessionCompleted: ((String) -> Unit)? = null,
) : AutoCloseable {
    private val root = root.canonicalFile
    private val options = captureOptions.validated()
    private val ingressLock = Any()
    private val queue = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, "Ansight Offline Capture").apply { isDaemon = true }
    }
    private var accepting = false
    private var queuedBytes = 0
    private var admissionMetadataBytes = (sessionHeader?.size?.toLong()?.plus(1) ?: 0) + (customProperties?.size ?: 0)
    internal val exportIdentifier: String get() = java.security.MessageDigest.getInstance("SHA-256")
        .digest(root.path.toByteArray(Charsets.UTF_8)).joinToString("") { "%02x".format(it) }
    private var droppedRecords = 0L
    private var failure: String? = null
    private var blockedByLease = false
    private class RetentionBlocked : IOException("Offline retention is blocked by active transfers or the current session.")
    private var closed = false
    // Accessed only by the IO worker.
    private var current: OfflineCaptureSession? = null
    private var writer: FileOutputStream? = null
    private val leases = mutableMapOf<String, String>()
    private var lastOrdinal = 0L
    private var recovered = false
    private var cachedSessions: MutableMap<String, OfflineCaptureSession>? = null

    init {
        require((sessionHeader?.size?.toLong()?.plus(1) ?: 0) + (customProperties?.size ?: 0) < options.maximumSessionBytes) { "Offline metadata exceeds the session budget." }
    }

    fun updateCustomProperties(json: ByteArray) = synchronized(ingressLock) {
        val metadataBytes = json.size + (sessionHeader?.size?.toLong()?.plus(1) ?: 0)
        if (closed || json.size > options.maximumQueuedBytes - queuedBytes || metadataBytes >= options.maximumSessionBytes) { droppedRecords++; return@synchronized }
        admissionMetadataBytes = metadataBytes
        val copy = json.copyOf()
        queuedBytes += copy.size
        queue.execute {
            try {
                runWrite {
                    require(copy.size + (sessionHeader?.size?.toLong()?.plus(1) ?: 0) < options.maximumSessionBytes)
                    val delta = copy.size.toLong() - (customProperties?.size ?: 0)
                    customProperties = copy
                    val session = current
                    if (session != null) {
                        if (session.bytes + delta > options.maximumSessionBytes) { seal("sizeLimit"); start() }
                        else {
                            enforceRetention(0, maxOf(0, delta))
                            val path = File(directory(session.sessionId), "metadata/custom-properties.json")
                            ensureDirectory(path.parentFile!!)
                            atomicWrite(path, copy)
                            current = session.copy(bytes = session.bytes + delta)
                            persistCurrent()
                        }
                    }
                }
            } finally { synchronized(ingressLock) { queuedBytes -= copy.size } }
        }
    }

    val isRecording: Boolean get() = synchronized(ingressLock) { accepting }
    val lastError: String? get() = synchronized(ingressLock) { failure }

    fun setRecording(enabled: Boolean) = synchronized(ingressLock) {
        check(!closed) { "Offline capture store is closed." }
        val requested = enabled && options.enabled
        if (accepting != requested) {
            accepting = requested
            queue.execute { runWrite { if (requested) start() else seal("normal") } }
        }
    }

    /** Accepts a complete host-compatible JSONL record without its trailing newline. */
    fun append(json: ByteArray): Boolean = enqueue(json, null)

    fun appendAttachment(data: ByteArray, extension: String, index: (String) -> ByteArray): Boolean {
        if (!extension.matches(Regex("[a-z0-9]+"))) return false
        val path = "attachments/${UUID.randomUUID()}.$extension"
        return enqueue(index(path), Attachment(path, data))
    }

    fun appendScreenshot(jpeg: ByteArray, capturedAtUtc: String, width: Int, height: Int, quality: Int, geometry: JSONObject? = null): Boolean {
        val path = "screenshots/${UUID.randomUUID()}.jpg"
        val index = JSONObject().put("path", path).put("capturedAtUtc", capturedAtUtc)
            .put("width", width).put("height", height).put("quality", quality)
        geometry?.let { index.put("geometry", it) }
        return enqueue(index.toString().toByteArray(Charsets.UTF_8), Attachment(path, jpeg), "screenshots/index/frames.jsonl")
    }

    fun flush(): Future<Unit> = perform {
        lastError?.let { throw IOException(it) }
        writer?.fd?.sync()
        persistCurrent()
    }

    fun sessions(): Future<List<OfflineCaptureSession>> = perform {
        recover()
        persistCurrent()
        readSessions()
    }

    fun acquire(sessionId: String): Future<OfflineCaptureLease> = perform {
        recover()
        check(current?.sessionId != sessionId && sessionId !in leases) { "Offline session is busy." }
        require(readSessions().any { it.sessionId == sessionId && it.stoppedAtUtc != null }) { "Unknown offline session." }
        val token = UUID.randomUUID().toString()
        leases[sessionId] = token
        OfflineCaptureLease(sessionId, token, directory(sessionId))
    }

    fun release(lease: OfflineCaptureLease): Future<Unit> = perform {
        validate(lease)
        leases.remove(lease.sessionId)
        enforceRetention(0, 0)
        resumeAfterLeaseChange()
    }

    /** Call only after validating the authenticated host's durable import receipt. */
    fun acknowledge(lease: OfflineCaptureLease, archiveSha256: String,
                    acknowledgedSha256: String): Future<Unit> = perform {
        validate(lease)
        require(archiveSha256.matches(Regex("[0-9a-f]{64}")) && archiveSha256 == acknowledgedSha256) {
            "Offline capture receipt does not match the exported archive."
        }
        deleteSession(directory(lease.sessionId))
        leases.remove(lease.sessionId)
        resumeAfterLeaseChange()
        Unit
    }

    private fun resumeAfterLeaseChange() {
        val resume = synchronized(ingressLock) {
            if (blockedByLease) { failure = null; blockedByLease = false; accepting } else false
        }
        if (resume) start()
    }

    override fun close() {
        synchronized(ingressLock) {
            if (closed) return
            accepting = false
            closed = true
            queue.execute { runWrite { seal("normal") } }
            queue.shutdown()
        }
    }

    private data class Attachment(val path: String, val data: ByteArray)

    private fun enqueue(json: ByteArray, attachment: Attachment?, indexPath: String? = null): Boolean = synchronized(ingressLock) {
        if (!accepting || closed) return false
        val byteCount = json.size.toLong() + 1L + (attachment?.data?.size ?: 0)
        if (byteCount > options.maximumQueuedBytes - queuedBytes || byteCount + admissionMetadataBytes > options.maximumSessionBytes) {
            droppedRecords++
            return false
        }
        // The producer retains its buffers; snapshot before admitting asynchronous work.
        val record = json.copyOf()
        val file = attachment?.copy(data = attachment.data.copyOf())
        queuedBytes += byteCount.toInt()
        queue.execute {
            try {
                runWrite {
                    if (current == null) start()
                    if (current!!.bytes + byteCount > options.maximumSessionBytes) {
                        seal("sizeLimit")
                        start()
                    }
                    enforceRetention(0, byteCount)
                    if (file != null) {
                        val destination = File(directory(current!!.sessionId), file.path)
                        ensureDirectory(destination.parentFile!!)
                        atomicWrite(destination, file.data)
                    }
                    if (indexPath != null) {
                        val indexFile = File(directory(current!!.sessionId), indexPath)
                        ensureDirectory(indexFile.parentFile!!)
                        FileOutputStream(indexFile, true).use { it.write(record); it.write(10) }
                    } else { writer!!.write(record); writer!!.write(10) }
                    current = current!!.copy(bytes = current!!.bytes + byteCount, records = current!!.records + 1)
                }
            } finally { synchronized(ingressLock) { queuedBytes -= byteCount.toInt() } }
        }
        true
    }

    private fun start() {
        if (current != null) return
        recover()
        enforceRetention(1, 0)
        lastOrdinal = maxOf(lastOrdinal + 1, System.currentTimeMillis() * 1000)
        val session = OfflineCaptureSession(UUID.randomUUID().toString(), now(), lastOrdinal,
            null, null, appId, processSessionId, 0, 0, 0)
        val folder = directory(session.sessionId)
        ensureDirectory(folder)
        writer = FileOutputStream(File(folder, "records.jsonl"))
        current = session
        persistCurrent()
        sessionHeader?.let { header ->
            require(header.size + 1L <= options.maximumSessionBytes) { "Offline session metadata exceeds the session budget." }
            enforceRetention(0, header.size + 1L)
            writer!!.write(header)
            writer!!.write(10)
            current = session.copy(bytes = header.size + 1L)
        }
        customProperties?.let { properties ->
            val path = File(folder, "metadata/custom-properties.json")
            ensureDirectory(path.parentFile!!)
            enforceRetention(0, properties.size.toLong())
            atomicWrite(path, properties)
            current = current!!.copy(bytes = current!!.bytes + properties.size)
        }
        persistCurrent()
        onSessionStarted?.invoke(session.sessionId, folder.path)
    }

    private fun seal(reason: String) {
        val session = current ?: return
        writer?.fd?.sync()
        writer?.close()
        writer = null
        current = session.copy(stoppedAtUtc = now(), terminationKind = reason)
        persistCurrent()
        onSessionCompleted?.invoke(session.sessionId)
        current = null
    }

    private fun persistCurrent() {
        val session = current ?: return
        current = session.copy(droppedRecords = synchronized(ingressLock) { droppedRecords })
        writeManifest(current!!)
    }

    private fun recover() {
        if (recovered) return
        ensureDirectory(File(root, "sessions"))
        for (session in readSessions()) {
            if (session.stoppedAtUtc == null && session.sessionId != current?.sessionId) {
                writeManifest(session.copy(stoppedAtUtc = now(), terminationKind = "interrupted"))
            }
        }
        lastOrdinal = maxOf(lastOrdinal, readSessions().maxOfOrNull { it.ordinal } ?: 0)
        cachedSessions = readSessions().associateBy { it.sessionId }.toMutableMap()
        recovered = true
        enforceRetention(0, 0)
    }

    private fun enforceRetention(reservingSessions: Int, reservingBytes: Long) {
        val sessions = readSessions()
        var count = sessions.size + reservingSessions
        var bytes = sessions.sumOf { it.bytes } + reservingBytes
        for (session in sessions) {
            if (count <= options.maximumSessions && bytes <= options.maximumRetainedBytes) return
            if (session.sessionId == current?.sessionId || session.sessionId in leases) continue
            deleteSession(directory(session.sessionId))
            count--
            bytes -= session.bytes
        }
        if (count > options.maximumSessions || bytes > options.maximumRetainedBytes) throw RetentionBlocked()
    }

    private fun readSessions(): List<OfflineCaptureSession> {
        cachedSessions?.let { cached ->
            val sessions = cached.toMutableMap()
            current?.let { sessions[it.sessionId] = it }
            return sessions.values.sortedWith(compareBy<OfflineCaptureSession> { it.ordinal }.thenBy { it.sessionId })
        }
        val sessionsRoot = File(root, "sessions")
        if (!sessionsRoot.exists()) return emptyList()
        val folders = sessionsRoot.listFiles() ?: throw IOException("Could not read offline capture sessions.")
        return folders.mapNotNull { folder ->
            if (!folder.isDirectory || folder.canonicalFile != folder.absoluteFile) return@mapNotNull null
            if (!folder.name.matches(Regex("([0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})"))) return@mapNotNull null
            val json = JSONObject(File(folder, "manifest.json").readText())
            val id = json.getString("sessionId")
            require(id == folder.name) { "Offline session identity does not match its directory." }
            if (current?.sessionId == id) return@mapNotNull current
            OfflineCaptureSession(id, json.getString("startedAtUtc"), json.optLong("ordinal", parseLegacyOrdinal(json.getString("startedAtUtc"))),
                json.nullableString("stoppedAtUtc"), json.nullableString("terminationKind"),
                json.getString("appId"), json.optString("processSessionId", "legacy"),
                payloadBytes(folder), json.optLong("records", 0), json.optLong("droppedRecords", json.optLong("droppedRecordCount", 0)))
        }.sortedWith(compareBy<OfflineCaptureSession> { it.ordinal }.thenBy { it.sessionId })
    }

    private fun parseLegacyOrdinal(value: String): Long {
        val match = Regex("^(\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2})(?:\\.(\\d+))?(Z|[+-]\\d{2}:\\d{2})$").matchEntire(value)
            ?: throw IOException("Invalid offline session timestamp.")
        val zone = match.groupValues[3].let { if (it == "Z") "+0000" else it.replace(":", "") }
        val format = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ssZ", Locale.US).apply { isLenient = false }
        val seconds = format.parse(match.groupValues[1] + zone)!!.time * 1000
        return seconds + match.groupValues[2].padEnd(6, '0').take(6).toLong()
    }

    private fun writeManifest(session: OfflineCaptureSession) {
        val path = File(directory(session.sessionId), "manifest.json")
        val json = (if (path.exists()) JSONObject(path.readText()) else JSONObject()).put("sessionId", session.sessionId).put("startedAtUtc", session.startedAtUtc)
            .put("ordinal", session.ordinal).put("stoppedAtUtc", session.stoppedAtUtc)
            .put("terminationKind", session.terminationKind).put("appId", session.appId)
            .put("processSessionId", session.processSessionId).put("bytes", session.bytes)
            .put("records", session.records).put("droppedRecords", session.droppedRecords)
        atomicWrite(File(directory(session.sessionId), "manifest.json"), json.toString().toByteArray(Charsets.UTF_8))
        cachedSessions?.put(session.sessionId, session)
    }

    private fun validate(lease: OfflineCaptureLease) {
        require(leases[lease.sessionId] == lease.token && lease.directory == directory(lease.sessionId) &&
            current?.sessionId != lease.sessionId) { "Invalid offline capture lease." }
    }
    private fun directory(id: String) = File(File(root, "sessions"), id)
    private fun payloadBytes(directory: File): Long = directory.walkTopDown()
        .onEnter { it.canonicalFile == it.absoluteFile }
        .filter { it.isFile && it.name != "manifest.json" && it.canonicalFile == it.absoluteFile }
        .sumOf { it.length() }
    private fun deleteSession(directory: File) {
        if (!directory.deleteRecursively()) throw IOException("Could not delete offline capture session.")
        cachedSessions?.remove(directory.name)
    }
    private fun runWrite(work: () -> Unit) {
        try { work() } catch (exception: Exception) {
            synchronized(ingressLock) {
                failure = exception.message
                blockedByLease = exception is RetentionBlocked
                droppedRecords++
            }
        }
    }
    private fun <T> perform(work: () -> T): Future<T> = synchronized(ingressLock) {
        check(!closed) { "Offline capture store is closed." }
        val future = FutureTask<T> { work() }
        queue.execute(future)
        future
    }

    private fun JSONObject.nullableString(key: String): String? = if (isNull(key)) null else getString(key)

    companion object {
        private fun now(): String = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US)
            .apply { timeZone = TimeZone.getTimeZone("UTC") }.format(Date())
        private fun ensureDirectory(directory: File) {
            if (!directory.isDirectory && !directory.mkdirs()) throw IOException("Could not create offline capture directory.")
        }
        private fun atomicWrite(file: File, data: ByteArray) {
            val temporary = File(file.parentFile, "${file.name}.tmp")
            try {
                FileOutputStream(temporary).use { it.write(data); it.fd.sync() }
                if (!temporary.renameTo(file)) throw IOException("Could not commit offline capture metadata.")
            } finally { temporary.delete() }
        }
    }
}
