package ai.ansight.runtime

import android.app.ActivityManager
import android.app.Application
import android.app.ApplicationExitInfo
import android.annotation.TargetApi
import android.os.Build
import android.os.Process
import android.util.Base64
import android.util.Log
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.nio.charset.Charset
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean

data class AnsightCrashCaptureOptions(
    val enabled: Boolean = true,
    val hostHandoffEnabled: Boolean = true,
    val offlineCaptureAttachmentEnabled: Boolean = true,
    val maximumPendingReports: Int = 8,
    val retentionDays: Int = 7,
    val maximumBreadcrumbs: Int = 64,
    val maximumTraceBytes: Int = 1_048_576,
) {
    fun validated(): AnsightCrashCaptureOptions = copy(
        maximumPendingReports = maximumPendingReports.coerceIn(1, 32),
        retentionDays = retentionDays.coerceIn(1, 30),
        maximumBreadcrumbs = maximumBreadcrumbs.coerceIn(0, 256),
        maximumTraceBytes = maximumTraceBytes.coerceIn(16 * 1024, 4 * 1024 * 1024),
    )
}

/**
 * Crash persistence is intentionally independent from the telemetry lock and transport. Fatal
 * handlers only write bounded app-private files; recovery and delivery happen in a healthy process.
 */
internal object AnsightCrashCapture {
    private const val Schema = "ansight.crash.v1"
    private const val RootDirectoryName = "ansight/crashes"
    private const val ActiveSessionFileName = "active-session.json"
    private const val BreadcrumbFileName = "breadcrumbs.jsonl"
    private val utf8 = Charset.forName("UTF-8")

    private val lifecycleLock = Any()
    private val handlingFatalException = AtomicBoolean(false)
    private var application: Application? = null
    private var options = AnsightCrashCaptureOptions(enabled = false)
    private var rootDirectory: File? = null
    private var previousUncaughtExceptionHandler: Thread.UncaughtExceptionHandler? = null
    private var handlerInstalled = false

    fun initialize(application: Application, options: AnsightCrashCaptureOptions) {
        synchronized(lifecycleLock) {
            this.application = application
            this.options = options.validated()
            rootDirectory = runCatching { resolveRootDirectory(application).also { it.mkdirs() } }.getOrNull()

            if (!this.options.enabled) {
                uninstallHandlerLocked()
                return
            }

            if (rootDirectory == null) return

            val signalEvidence = AndroidCrashSignalBridge.consume(signalFile(rootDirectory!!))
            recoverPreviousProcessLocked(signalEvidence)
            beginCurrentProcessLocked()
            installHandlerLocked()
            AndroidCrashSignalBridge.install(signalFile(rootDirectory!!))
            trimPendingReportsLocked()
        }
    }

    fun processSessionId(): String = ProcessSessionIdentity.current

    fun recordCandidate(
        runtime: String,
        kind: String,
        message: String?,
        stack: String?,
        fatal: Boolean,
        metadata: JSONObject? = null,
    ): String? {
        if (!options.enabled) return null
        val root = rootDirectory ?: return null
        val candidateId = UUID.randomUUID().toString()
        val candidate = JSONObject()
            .put("candidateId", candidateId)
            .put("processSessionId", ProcessSessionIdentity.current)
            .put("occurredAtUtc", AnsightClock.isoNow())
            .put("occurredAtEpochMs", System.currentTimeMillis())
            .put("runtime", runtime.trim().ifBlank { "android" })
            .put("kind", kind.trim().ifBlank { "unhandled_exception" })
            .put("fatal", fatal)
            .putNullable("message", message?.take(16_384))
            .putNullable("stack", stack?.take(128 * 1024))
            .putNullable("metadata", metadata)
        writeAtomic(File(rawDirectory(root), "$candidateId.json"), candidate.toString())
        return candidateId
    }

    fun recordBreadcrumb(kind: String, label: String, details: String? = null) {
        val currentOptions = options
        if (!currentOptions.enabled || currentOptions.maximumBreadcrumbs == 0) return
        val root = rootDirectory ?: return
        val line = JSONObject()
            .put("capturedAtUtc", AnsightClock.isoNow())
            .put("kind", kind.take(64))
            .put("label", label.take(512))
            .putNullable("details", details?.take(4_096))
            .toString()
        synchronized(lifecycleLock) {
            val file = File(root, BreadcrumbFileName)
            val retained = if (file.exists()) {
                file.readLines().takeLast((currentOptions.maximumBreadcrumbs - 1).coerceAtLeast(0))
            } else {
                emptyList()
            }
            writeAtomic(file, (retained + line).joinToString("\n", postfix = "\n"))
        }
    }

    fun associateHostSession(hostId: String?, configId: String?, appId: String?) {
        updateActiveSession { active ->
            active.put("hostSessionId", ProcessSessionIdentity.current)
            active.putNullable("hostId", hostId)
            active.putNullable("configId", configId)
            active.putNullable("appId", appId)
            active.put("hostOpenedAtUtc", AnsightClock.isoNow())
            active.remove("hostCompletedAtUtc")
        }
    }

    fun markHostSessionCompleted() {
        updateActiveSession { active -> active.put("hostCompletedAtUtc", AnsightClock.isoNow()) }
    }

    fun associateOfflineSession(sessionId: String, directory: String?) {
        val normalized = sessionId.trim()
        if (normalized.isEmpty()) return
        updateActiveSession { active ->
            active.put("offlineSessionId", normalized)
            active.putNullable("offlineSessionDirectory", directory?.trim()?.ifBlank { null })
            active.put("offlineStartedAtUtc", AnsightClock.isoNow())
            active.remove("offlineCompletedAtUtc")
        }
    }

    fun markOfflineSessionCompleted(sessionId: String) {
        updateActiveSession { active ->
            if (active.optString("offlineSessionId") == sessionId) {
                active.put("offlineCompletedAtUtc", AnsightClock.isoNow())
            }
        }
    }

    fun pendingReportsJson(): String {
        val reports = pendingReportFiles().mapNotNull(::readJson)
        return JSONObject()
            .put("processSessionId", ProcessSessionIdentity.current)
            .put("reports", JSONArray(reports))
            .toString()
    }

    fun markOfflineReportPersisted(reportId: String): Boolean = updatePendingReport(reportId) { report ->
        report.put("offlineCapturePersisted", true)
    }

    fun deliverPendingReports(transport: PairingLiveSessionTransport) {
        if (!options.enabled || !options.hostHandoffEnabled) return
        for (file in pendingReportFiles()) {
            val report = readJson(file) ?: continue
            if (report.optBoolean("hostAcknowledged", false)) {
                deleteIfFullyDelivered(file, report)
                continue
            }

            val result = transport.sendControlRequestWithResponse(
                "crash.handoff",
                JSONObject()
                    .put("reportId", report.optString("reportId"))
                    .put("targetProcessSessionId", report.optString("previousProcessSessionId"))
                    .putNullable("targetSessionId", report.optionalString("hostSessionId"))
                    .put("deliveryProcessSessionId", ProcessSessionIdentity.current)
                    .put("report", report),
            ).operationResult
            if (result.success) {
                report.put("hostAcknowledged", true)
                report.put("hostAcknowledgedAtUtc", AnsightClock.isoNow())
                writeAtomic(file, report.toString())
                deleteIfFullyDelivered(file, report)
            }
        }
    }

    private fun recoverPreviousProcessLocked(signalEvidence: AndroidCrashSignalEvidence?) {
        val root = rootDirectory ?: return
        val activeFile = File(root, ActiveSessionFileName)
        val previousSession = readJson(activeFile) ?: return
        val previousProcessSessionId = previousSession.optionalString("processSessionId") ?: return
        if (previousProcessSessionId == ProcessSessionIdentity.current) return
        val previousProcessId = previousSession.optInt("processId", 0)
        val matchingSignalEvidence = signalEvidence?.takeIf {
            previousProcessId == 0 || it.processId == previousProcessId
        }

        File(historyDirectory(root), "$previousProcessSessionId.json").let { historyFile ->
            writeAtomic(historyFile, previousSession.toString())
        }

        val candidates = rawDirectory(root)
            .listFiles { file -> file.extension == "json" }
            .orEmpty()
            .mapNotNull(::readJson)
            .filter { it.optString("processSessionId") == previousProcessSessionId }
            .sortedBy { it.optLong("occurredAtEpochMs") }

        val exit = findPreviousExit(previousSession)
        if (exit == null && matchingSignalEvidence == null && candidates.none { it.optBoolean("fatal", false) }) return

        val latestCandidate = if (exit == null) {
            candidates.lastOrNull { it.optBoolean("fatal", false) }
        } else {
            candidates.lastOrNull()
        }
        val occurredAtEpochMs = exit?.let(::exitTimestamp)
            ?: matchingSignalEvidence?.occurredAtEpochMs
            ?: latestCandidate?.optLong("occurredAtEpochMs")
            ?: System.currentTimeMillis()
        val reason = exit?.let(::exitReasonName)
            ?: matchingSignalEvidence?.kind
            ?: latestCandidate?.optString("kind")
            ?: "unknown"
        val reportId = stableReportId(previousProcessSessionId, occurredAtEpochMs, reason)
        val reportFile = File(pendingDirectory(root), "$reportId.json")
        if (!reportFile.exists()) {
            val traceBase64 = exit?.let(::readExitTrace)
            val report = JSONObject()
                .put("schema", Schema)
                .put("reportId", reportId)
                .put("previousProcessSessionId", previousProcessSessionId)
                .put("occurredAtUtc", AnsightClock.isoAt(occurredAtEpochMs))
                .put("detectedAtUtc", AnsightClock.isoNow())
                .put("platform", "android")
                .put("kind", reason)
                .put("confidence", if (exit != null || latestCandidate?.optBoolean("fatal") == true) "confirmed" else "inferred")
                .putNullable("candidate", latestCandidate)
                .putNullable("termination", exit?.let(::exitJson) ?: matchingSignalEvidence?.toJson())
                .putNullable("traceBase64", traceBase64)
                .put("breadcrumbs", readBreadcrumbs(root))
                .putNullable("hostSessionId", previousSession.optionalString("hostSessionId"))
                .putNullable("hostId", previousSession.optionalString("hostId"))
                .putNullable("configId", previousSession.optionalString("configId"))
                .putNullable("appId", previousSession.optionalString("appId"))
                .putNullable("offlineSessionId", previousSession.optionalString("offlineSessionId"))
                .putNullable("offlineSessionDirectory", previousSession.optionalString("offlineSessionDirectory"))
                .put("hostRequired", options.hostHandoffEnabled)
                .put(
                    "offlineCaptureRequired",
                    options.offlineCaptureAttachmentEnabled &&
                        previousSession.optionalString("offlineSessionId") != null &&
                        previousSession.optionalString("offlineCompletedAtUtc") == null,
                )
                .put("hostAcknowledged", false)
                .put("offlineCapturePersisted", false)
            writeAtomic(reportFile, report.toString())
        }

        rawDirectory(root).listFiles().orEmpty().forEach { raw ->
            val candidate = readJson(raw)
            if (candidate?.optString("processSessionId") == previousProcessSessionId) raw.delete()
        }
    }

    private fun beginCurrentProcessLocked() {
        val root = rootDirectory ?: return
        val app = application ?: return
        val packageInfo = runCatching { app.packageManager.getPackageInfo(app.packageName, 0) }.getOrNull()
        val buildNumber = packageInfo?.let { info ->
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                info.longVersionCode
            } else {
                @Suppress("DEPRECATION")
                info.versionCode.toLong()
            }
        } ?: 0L
        val active = JSONObject()
            .put("processSessionId", ProcessSessionIdentity.current)
            .put("processId", Process.myPid())
            .put(
                "processName",
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) Application.getProcessName()
                else app.applicationInfo.processName,
            )
            .put("startedAtUtc", AnsightClock.isoNow())
            .put("startedAtEpochMs", System.currentTimeMillis())
            .put("appId", app.packageName)
            .putNullable("appVersion", packageInfo?.versionName)
            .put("buildNumber", buildNumber)
        writeAtomic(File(root, ActiveSessionFileName), active.toString())
        writeAtomic(File(root, BreadcrumbFileName), "")
    }

    private fun installHandlerLocked() {
        if (handlerInstalled) return
        previousUncaughtExceptionHandler = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            if (handlingFatalException.compareAndSet(false, true)) {
                runCatching {
                    recordCandidate(
                        runtime = "android-jvm",
                        kind = "unhandled_exception",
                        message = throwable.message ?: throwable.javaClass.name,
                        stack = Log.getStackTraceString(throwable),
                        fatal = true,
                        metadata = JSONObject()
                            .put("threadName", thread.name)
                            .put("threadId", thread.id),
                    )
                }
            }
            previousUncaughtExceptionHandler?.uncaughtException(thread, throwable)
        }
        handlerInstalled = true
    }

    private fun uninstallHandlerLocked() {
        if (!handlerInstalled) return
        Thread.setDefaultUncaughtExceptionHandler(previousUncaughtExceptionHandler)
        previousUncaughtExceptionHandler = null
        handlerInstalled = false
    }

    private fun updateActiveSession(update: (JSONObject) -> Unit) {
        if (!options.enabled) return
        synchronized(lifecycleLock) {
            val root = rootDirectory ?: return
            val file = File(root, ActiveSessionFileName)
            val active = readJson(file) ?: JSONObject()
            update(active)
            writeAtomic(file, active.toString())
        }
    }

    private fun updatePendingReport(reportId: String, update: (JSONObject) -> Unit): Boolean {
        val normalized = reportId.trim()
        if (normalized.isEmpty()) return false
        synchronized(lifecycleLock) {
            val file = pendingReportFiles().firstOrNull { it.nameWithoutExtension == normalized } ?: return false
            val report = readJson(file) ?: return false
            update(report)
            writeAtomic(file, report.toString())
            deleteIfFullyDelivered(file, report)
            return true
        }
    }

    private fun deleteIfFullyDelivered(file: File, report: JSONObject) {
        val hostDelivered = !report.optBoolean("hostRequired", true) || report.optBoolean("hostAcknowledged")
        val offlineDelivered = !report.optBoolean("offlineCaptureRequired", false) || report.optBoolean("offlineCapturePersisted")
        if (hostDelivered && offlineDelivered) file.delete()
    }

    private fun findPreviousExit(previousSession: JSONObject): ApplicationExitInfo? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) return null
        val app = application ?: return null
        val manager = app.getSystemService(ActivityManager::class.java) ?: return null
        val startedAtEpochMs = previousSession.optLong("startedAtEpochMs", 0L)
        val processId = previousSession.optInt("processId", 0)
        val processName = previousSession.optionalString("processName")
        return runCatching {
            manager.getHistoricalProcessExitReasons(app.packageName, processId, 16)
                .filter { it.timestamp >= startedAtEpochMs }
                .filter { processName == null || it.processName == processName }
                .filter {
                    it.reason == ApplicationExitInfo.REASON_CRASH ||
                        it.reason == ApplicationExitInfo.REASON_CRASH_NATIVE ||
                        it.reason == ApplicationExitInfo.REASON_ANR ||
                        it.reason == ApplicationExitInfo.REASON_LOW_MEMORY ||
                        it.reason == ApplicationExitInfo.REASON_SIGNALED ||
                        it.reason == ApplicationExitInfo.REASON_INITIALIZATION_FAILURE ||
                        it.reason == ApplicationExitInfo.REASON_EXCESSIVE_RESOURCE_USAGE
                }
                .maxByOrNull { it.timestamp }
        }.getOrNull()
    }

    @TargetApi(Build.VERSION_CODES.R)
    private fun exitTimestamp(exit: ApplicationExitInfo): Long = exit.timestamp

    @TargetApi(Build.VERSION_CODES.R)
    private fun exitReasonName(exit: ApplicationExitInfo): String = when (exit.reason) {
        ApplicationExitInfo.REASON_CRASH -> "managed_exception"
        ApplicationExitInfo.REASON_CRASH_NATIVE -> "native_crash"
        ApplicationExitInfo.REASON_ANR -> "anr"
        ApplicationExitInfo.REASON_LOW_MEMORY -> "low_memory"
        ApplicationExitInfo.REASON_SIGNALED -> "signal"
        ApplicationExitInfo.REASON_INITIALIZATION_FAILURE -> "initialization_failure"
        ApplicationExitInfo.REASON_EXCESSIVE_RESOURCE_USAGE -> "excessive_resource_usage"
        else -> "abnormal_exit"
    }

    @TargetApi(Build.VERSION_CODES.R)
    private fun exitJson(exit: ApplicationExitInfo): JSONObject = JSONObject()
        .put("reason", exitReasonName(exit))
        .put("reasonCode", exit.reason)
        .put("status", exit.status)
        .put("importance", exit.importance)
        .put("pssKb", exit.pss)
        .put("rssKb", exit.rss)
        .putNullable("description", exit.description)

    @TargetApi(Build.VERSION_CODES.R)
    private fun readExitTrace(exit: ApplicationExitInfo): String? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) return null
        return runCatching {
            exit.traceInputStream?.use { input ->
                val output = ByteArrayOutputStream(minOf(options.maximumTraceBytes, 64 * 1024))
                val buffer = ByteArray(16 * 1024)
                while (output.size() < options.maximumTraceBytes) {
                    val length = input.read(
                        buffer,
                        0,
                        minOf(buffer.size, options.maximumTraceBytes - output.size()),
                    )
                    if (length <= 0) break
                    output.write(buffer, 0, length)
                }
                val bytes = output.toByteArray()
                if (bytes.isEmpty()) null else Base64.encodeToString(bytes, Base64.NO_WRAP)
            }
        }.getOrNull()
    }

    private fun readBreadcrumbs(root: File): JSONArray {
        val file = File(root, BreadcrumbFileName)
        if (!file.exists()) return JSONArray()
        return JSONArray(file.readLines().takeLast(options.maximumBreadcrumbs).mapNotNull { line ->
            runCatching { JSONObject(line) }.getOrNull()
        })
    }

    private fun pendingReportFiles(): List<File> = rootDirectory
        ?.let(::pendingDirectory)
        ?.listFiles { file -> file.extension == "json" }
        .orEmpty()
        .sortedBy { it.lastModified() }

    private fun trimPendingReportsLocked() {
        val cutoff = System.currentTimeMillis() - options.retentionDays * 86_400_000L
        val files = pendingReportFiles()
        files.filter { it.lastModified() < cutoff }.forEach(File::delete)
        pendingReportFiles().dropLast(options.maximumPendingReports).forEach(File::delete)
    }

    private fun rawDirectory(root: File): File = File(root, "raw").also { it.mkdirs() }

    private fun pendingDirectory(root: File): File = File(root, "pending").also { it.mkdirs() }

    private fun historyDirectory(root: File): File = File(root, "sessions").also { it.mkdirs() }

    private fun signalFile(root: File): File = File(root, "signal.raw")

    private fun resolveRootDirectory(application: Application): File {
        val base = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            application.noBackupFilesDir
        } else {
            application.filesDir
        }
        return File(base, RootDirectoryName)
    }

    private fun readJson(file: File): JSONObject? = runCatching {
        if (!file.exists()) null else JSONObject(file.readText(utf8))
    }.getOrNull()

    private fun writeAtomic(file: File, content: String) {
        runCatching {
            file.parentFile?.mkdirs()
            val temporary = File(file.parentFile, ".${file.name}.${UUID.randomUUID()}.tmp")
            FileOutputStream(temporary).use { stream ->
                stream.write(content.toByteArray(utf8))
                stream.fd.sync()
            }
            if (!temporary.renameTo(file)) {
                temporary.copyTo(file, overwrite = true)
                temporary.delete()
            }
        }
    }

    private fun stableReportId(processSessionId: String, occurredAtEpochMs: Long, reason: String): String {
        val bytes = "$processSessionId:$occurredAtEpochMs:$reason".toByteArray(utf8)
        return MessageDigest.getInstance("SHA-256")
            .digest(bytes)
            .take(16)
            .joinToString("") { byte -> "%02x".format(byte) }
    }
}
