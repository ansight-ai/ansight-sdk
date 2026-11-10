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
internal open class AnsightCrashCapture(
    private val currentProcessSessionId: String = ProcessSessionIdentity.current,
    private val configuredRootDirectory: File? = null,
    private val installPlatformHandlers: Boolean = true,
    private val readSignal: (File) -> AndroidCrashSignalEvidence? = AndroidCrashSignalBridge::read,
) {
    companion object : AnsightCrashCapture()
    private val Schema = "ansight.crash.v1"
    private val RootDirectoryName = "ansight/crashes"
    private val ActiveSessionFileName = "active-session.json"
    private val BreadcrumbFileName = "breadcrumbs.jsonl"
    private val utf8 = Charset.forName("UTF-8")

    private val lifecycleLock = Any()
    private val handlingFatalException = AtomicBoolean(false)
    private var application: Application? = null
    @Volatile private var options = AnsightCrashCaptureOptions(enabled = false)
    @Volatile private var rootDirectory: File? = null
    private var previousUncaughtExceptionHandler: Thread.UncaughtExceptionHandler? = null
    private var installedUncaughtExceptionHandler: Thread.UncaughtExceptionHandler? = null
    private var handlerInstalled = false
    @Volatile private var processInitialized = false
    private var delivering = false

    fun initialize(application: Application, options: AnsightCrashCaptureOptions) {
        synchronized(lifecycleLock) {
            this.application = application
            this.options = options.validated()
            rootDirectory = configuredRootDirectory ?: runCatching { resolveRootDirectory(application) }.getOrNull()
            if (!this.options.enabled) {
                if (installPlatformHandlers) {
                    uninstallHandlerLocked()
                    AndroidCrashSignalBridge.uninstall()
                }
                return
            }
            val root = rootDirectory ?: return
            root.mkdirs()
            if (!root.isDirectory) return
            trimEvidenceLocked(root)
            if (!processInitialized) {
                if (!archivePreviousSessionLocked(root)) return
                recoverPreviousProcessesLocked(root)
                if (!beginCurrentProcessLocked()) return
                processInitialized = true
            } else {
                recoverPreviousProcessesLocked(root)
            }
            if (installPlatformHandlers) {
                installHandlerLocked()
                AndroidCrashSignalBridge.install(signalFile(root, currentProcessSessionId))
            }
            trimEvidenceLocked(root)
        }
    }

    fun processSessionId(): String = currentProcessSessionId
    val offlineAttachmentEnabled: Boolean get() = options.enabled && options.offlineCaptureAttachmentEnabled

    fun recordCandidate(
        runtime: String,
        kind: String,
        message: String?,
        stack: String?,
        fatal: Boolean,
        metadata: JSONObject? = null,
    ): String? {
        if (!options.enabled || !processInitialized) return null
        val root = rootDirectory ?: return null
        val candidateId = UUID.randomUUID().toString()
        val candidate = JSONObject()
            .put("candidateId", candidateId)
            .put("processSessionId", currentProcessSessionId)
            .put("occurredAtUtc", AnsightClock.isoNow())
            .put("occurredAtEpochMs", System.currentTimeMillis())
            .put("runtime", runtime.trim().ifBlank { "android" }.take(128))
            .put("kind", kind.trim().ifBlank { "unhandled_exception" }.take(128))
            .put("fatal", fatal)
            .putNullable("message", message?.take(16_384))
            .putNullable("stack", stack?.take(128 * 1024))
            .putNullable("metadata", metadata?.takeIf { it.toString().toByteArray(utf8).size <= 16_384 })
        if (!writeAtomic(File(rawDirectory(root), "$candidateId.json"), candidate.toString())) return null
        trimRawCandidates(root, currentProcessSessionId, 16)
        trimRawCandidates(root, null, options.maximumPendingReports * 16)
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
            active.put("hostSessionId", currentProcessSessionId)
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
        val reports = synchronized(lifecycleLock) { pendingReportFiles().mapNotNull(::readJson) }
        return JSONObject()
            .put("processSessionId", currentProcessSessionId)
            .put("reports", JSONArray(reports))
            .toString()
    }

    fun markOfflineReportPersisted(reportId: String): Boolean = updatePendingReport(reportId) { report ->
        report.put("offlineCapturePersisted", true)
    }

    fun deliverPendingReports(transport: PairingLiveSessionTransport) {
        synchronized(lifecycleLock) {
            val deliveryUnavailable = !options.enabled
                || !options.hostHandoffEnabled
                || delivering
            if (deliveryUnavailable) return
            delivering = true
        }
        try {
            for (file in pendingReportFiles()) {
                val report = readJson(file) ?: continue
                if (report.optBoolean("hostAcknowledged", false)) continue
                val reportId = report.optString("reportId")
                val result = transport.sendControlRequestWithResponse(
                    "crash.handoff",
                    JSONObject()
                        .put("reportId", reportId)
                        .put("targetProcessSessionId", report.optString("previousProcessSessionId"))
                        .putNullable("targetSessionId", report.optionalString("hostSessionId"))
                        .put("deliveryProcessSessionId", currentProcessSessionId)
                        .put("report", report),
                ).operationResult
                if (result.success) {
                    updatePendingReport(reportId) {
                        it.put("hostAcknowledged", true)
                        it.put("hostAcknowledgedAtUtc", AnsightClock.isoNow())
                    }
                }
            }
        } finally {
            synchronized(lifecycleLock) { delivering = false }
        }
    }

    private fun archivePreviousSessionLocked(root: File): Boolean {
        val previous = readJson(File(root, ActiveSessionFileName)) ?: return true
        val id = previous.optionalString("processSessionId") ?: return true
        if (id == currentProcessSessionId) return true
        if (!isSafeIdentifier(id)) return false
        val history = File(historyDirectory(root), "$id.json")
        if (history.exists()) return true
        previous.put("endedBeforeEpochMs", System.currentTimeMillis())
        previous.put("breadcrumbs", readBreadcrumbs(root))
        return writeAtomic(history, previous.toString())
    }

    private fun recoverPreviousProcessesLocked(root: File) {
        historyDirectory(root).listFiles { file -> file.extension == "json" }.orEmpty()
            .mapNotNull(::readJson).forEach { recoverSessionLocked(root, it) }
    }

    private fun recoverSessionLocked(root: File, previousSession: JSONObject) {
        val previousProcessSessionId = previousSession.optionalString("processSessionId") ?: return
        if (!isSafeIdentifier(previousProcessSessionId) || previousProcessSessionId == currentProcessSessionId) return
        if (previousSession.has("crashReportId")) return
        val signalPath = signalFile(root, previousProcessSessionId)
        val signalSource = if (signalPath.exists()) signalPath else File(root, "signal.raw")
        val previousProcessId = previousSession.optInt("processId", 0)
        val signalEvidence = readSignal(signalSource)?.takeIf {
            previousProcessId == 0 || it.processId == previousProcessId
        }
        val candidateFiles = rawDirectory(root).listFiles { file -> file.extension == "json" }.orEmpty()
            .filter { readJson(it)?.optString("processSessionId") == previousProcessSessionId }
        val candidates = candidateFiles.mapNotNull(::readJson).sortedBy { it.optLong("occurredAtEpochMs") }
        val fatalCandidate = candidates.lastOrNull { it.optBoolean("fatal", false) }
        val exit = findPreviousExit(previousSession)
        val confirmed = exit != null || signalEvidence != null || fatalCandidate != null
        if (!confirmed) return
        val candidate = candidates.lastOrNull { it.optString("runtime") == "dotnet" } ?: fatalCandidate ?: candidates.lastOrNull()
        val occurredAtEpochMs = exit?.let(::exitTimestamp)
            ?: signalEvidence?.occurredAtEpochMs ?: fatalCandidate?.optLong("occurredAtEpochMs") ?: 0L
        val reason = exit?.let(::exitReasonName) ?: signalEvidence?.kind ?: fatalCandidate?.optString("kind") ?: "abnormal_exit"
        val reportId = stableReportId(previousProcessSessionId, occurredAtEpochMs, reason)
        val reportFile = File(pendingDirectory(root), "$reportId.json")
        if (readJson(reportFile) == null) {
            val requiresOfflineCapture = options.offlineCaptureAttachmentEnabled
                && previousSession.optionalString("offlineSessionId") != null
                && previousSession.optionalString("offlineCompletedAtUtc") == null
            val report = JSONObject()
                .put("schema", Schema).put("reportId", reportId)
                .put("previousProcessSessionId", previousProcessSessionId)
                .put("occurredAtUtc", AnsightClock.isoAt(occurredAtEpochMs))
                .put("detectedAtUtc", AnsightClock.isoNow())
                .put("platform", "android").put("kind", reason).put("confidence", "confirmed")
                .putNullable("candidate", candidate).put("candidates", JSONArray(candidates))
                .putNullable("termination", exit?.let(::exitJson) ?: signalEvidence?.toJson())
                .putNullable("traceBase64", exit?.let(::readExitTrace))
                .put("breadcrumbs", previousSession.optJSONArray("breadcrumbs") ?: JSONArray())
                .put("hostRequired", options.hostHandoffEnabled)
                .put("offlineCaptureRequired", requiresOfflineCapture)
                .put("hostAcknowledged", false).put("offlineCapturePersisted", false)
            for (key in listOf("hostSessionId", "hostId", "configId", "appId", "offlineSessionId", "offlineSessionDirectory", "appVersion", "buildNumber")) {
                report.putNullable(key, previousSession.opt(key))
            }
            if (!writeReport(reportFile, report)) return
        }
        previousSession.put("crashReportId", reportId)
        if (!writeAtomic(File(historyDirectory(root), "$previousProcessSessionId.json"), previousSession.toString())) return
        candidateFiles.forEach(File::delete)
        if (signalEvidence != null) signalSource.delete()
    }

    private fun beginCurrentProcessLocked(): Boolean {
        val root = rootDirectory ?: return false
        val app = application ?: return false
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
            .put("processSessionId", currentProcessSessionId)
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
        if (!writeAtomic(File(root, ActiveSessionFileName), active.toString())) return false
        writeAtomic(File(root, BreadcrumbFileName), "")
        return true
    }

    private fun installHandlerLocked() {
        if (handlerInstalled) return
        previousUncaughtExceptionHandler = Thread.getDefaultUncaughtExceptionHandler()
        val previousHandler = previousUncaughtExceptionHandler
        val handler = Thread.UncaughtExceptionHandler { thread, throwable ->
            val ownsCapture = handlingFatalException.compareAndSet(false, true)
            if (ownsCapture) {
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
            try {
                previousHandler?.uncaughtException(thread, throwable)
            } finally {
                if (ownsCapture) handlingFatalException.set(false)
            }
        }
        installedUncaughtExceptionHandler = handler
        Thread.setDefaultUncaughtExceptionHandler(handler)
        handlerInstalled = true
    }

    private fun uninstallHandlerLocked() {
        if (!handlerInstalled) return
        if (Thread.getDefaultUncaughtExceptionHandler() === installedUncaughtExceptionHandler) {
            Thread.setDefaultUncaughtExceptionHandler(previousUncaughtExceptionHandler)
        }
        previousUncaughtExceptionHandler = null
        installedUncaughtExceptionHandler = null
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
            if (!writeAtomic(file, report.toString())) return false
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
                .filter {
                    val withinProcess = it.timestamp >= startedAtEpochMs &&
                        it.timestamp <= previousSession.optLong("endedBeforeEpochMs", System.currentTimeMillis())
                    val retained = it.timestamp >= System.currentTimeMillis() - options.retentionDays * 86_400_000L
                    withinProcess && retained
                }
                .filter { processName == null || it.processName == processName }
                .filter {
                    val relevantExit = it.reason == ApplicationExitInfo.REASON_CRASH ||
                        it.reason == ApplicationExitInfo.REASON_CRASH_NATIVE ||
                        it.reason == ApplicationExitInfo.REASON_ANR ||
                        it.reason == ApplicationExitInfo.REASON_LOW_MEMORY ||
                        it.reason == ApplicationExitInfo.REASON_SIGNALED ||
                        it.reason == ApplicationExitInfo.REASON_INITIALIZATION_FAILURE ||
                        it.reason == ApplicationExitInfo.REASON_EXCESSIVE_RESOURCE_USAGE
                    relevantExit
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

    private fun trimEvidenceLocked(root: File) {
        val cutoff = System.currentTimeMillis() - options.retentionDays * 86_400_000L
        trimFiles(pendingReportFiles(), options.maximumPendingReports, cutoff)
        trimFiles(historyDirectory(root).listFiles().orEmpty().toList(), options.maximumPendingReports * 4, cutoff)
        trimFiles(File(root, "signals").listFiles().orEmpty().filter { it.nameWithoutExtension != currentProcessSessionId },
            options.maximumPendingReports * 4, cutoff)
        trimRawCandidates(root, null, options.maximumPendingReports * 16)
        rawDirectory(root).listFiles().orEmpty().filter { it.lastModified() < cutoff }.forEach(File::delete)
    }

    private fun trimFiles(files: List<File>, maximum: Int, cutoff: Long) {
        files.sortedByDescending { it.lastModified() }.forEachIndexed { index, file ->
            if (index >= maximum || file.lastModified() < cutoff) file.delete()
        }
    }

    private fun trimRawCandidates(root: File, processSessionId: String?, maximum: Int) {
        rawDirectory(root).listFiles { file -> file.extension == "json" }.orEmpty()
            .filter { processSessionId == null || readJson(it)?.optString("processSessionId") == processSessionId }
            .sortedWith(compareByDescending<File> { readJson(it)?.optBoolean("fatal", false) == true }
                .thenByDescending { it.lastModified() })
            .drop(maximum).forEach(File::delete)
    }

    private fun isSafeIdentifier(value: String): Boolean {
        if (value.isEmpty() || value.length > 128) return false
        return value.all { character ->
            val letter = character in 'a'..'z' || character in 'A'..'Z'
            val digit = character in '0'..'9'
            val separator = character == '-' || character == '_'
            val allowed = letter
                || digit
                || separator
            allowed
        }
    }

    private fun rawDirectory(root: File): File = File(root, "raw").also { it.mkdirs() }

    private fun pendingDirectory(root: File): File = File(root, "pending").also { it.mkdirs() }

    private fun historyDirectory(root: File): File = File(root, "sessions").also { it.mkdirs() }

    private fun signalFile(root: File, processSessionId: String): File =
        File(File(root, "signals").also { it.mkdirs() }, "$processSessionId.raw")

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

    private fun writeReport(file: File, report: JSONObject): Boolean {
        while (report.toString().toByteArray(utf8).size > 7 * 1024 * 1024) {
            val candidates = report.optJSONArray("candidates")
            val breadcrumbs = report.optJSONArray("breadcrumbs")
            if (candidates != null && candidates.length() > 0) {
                report.put("candidates", JSONArray((candidates.length() - candidates.length() / 2 until candidates.length()).map { candidates.get(it) }))
                report.put("candidatesTruncated", true)
            } else if (breadcrumbs != null && breadcrumbs.length() > 0) {
                report.put("breadcrumbs", JSONArray((breadcrumbs.length() - breadcrumbs.length() / 2 until breadcrumbs.length()).map { breadcrumbs.get(it) }))
                report.put("breadcrumbsTruncated", true)
            } else if (report.has("traceBase64")) {
                report.remove("traceBase64")
                report.put("traceOmittedForReportLimit", true)
            } else {
                return false
            }
        }
        return writeAtomic(file, report.toString())
    }

    private fun writeAtomic(file: File, content: String): Boolean {
        val temporary = File(file.parentFile, ".${file.name}.${UUID.randomUUID()}.tmp")
        return try {
            file.parentFile?.mkdirs()
            FileOutputStream(temporary).use { stream ->
                stream.write(content.toByteArray(utf8))
                stream.fd.sync()
            }
            temporary.renameTo(file)
        } catch (_: Exception) {
            false
        } finally {
            temporary.delete()
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
