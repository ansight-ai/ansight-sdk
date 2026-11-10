package ai.ansight.runtime

import android.app.Application
import android.os.Process
import java.io.File
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(manifest = Config.NONE, sdk = [28])
class AnsightCrashCaptureTest {
    @get:Rule val temporary = TemporaryFolder()
    private val application: Application get() = RuntimeEnvironment.getApplication()

    private fun capture(root: File, id: String, options: AnsightCrashCaptureOptions = AnsightCrashCaptureOptions(),
                        readSignal: (File) -> AndroidCrashSignalEvidence? = { null }): AnsightCrashCapture =
        AnsightCrashCapture(id, root, false, readSignal).also { it.initialize(application, options) }

    private fun record(capture: AnsightCrashCapture, fatal: Boolean, runtime: String = "dotnet") {
        assertNotNull(capture.recordCandidate(runtime, "unhandled_exception", "failure", "managed stack", fatal))
    }

    private fun reports(capture: AnsightCrashCapture): List<JSONObject> {
        val reports = JSONObject(capture.pendingReportsJson()).getJSONArray("reports")
        return (0 until reports.length()).map { reports.getJSONObject(it) }
    }

    @Test fun laterNativeCandidateKeepsManagedStackAndAllCandidates() {
        val root = temporary.newFolder()
        val first = capture(root, "first")
        record(first, false)
        record(first, true, "android-jvm")
        val report = reports(capture(root, "second")).single()
        assertEquals("dotnet", report.getJSONObject("candidate").getString("runtime"))
        assertEquals(2, report.getJSONArray("candidates").length())
    }

    @Test fun writeFailureRetainsEvidenceAndRetriesAcrossAnotherLaunch() {
        val root = temporary.newFolder()
        val first = capture(root, "first")
        first.recordBreadcrumb("event", "before crash")
        record(first, true)
        val pending = File(root, "pending")
        assertTrue(pending.delete())
        pending.writeText("blocked")
        val second = capture(root, "second")
        assertTrue(reports(second).isEmpty())
        assertEquals(1, File(root, "raw").listFiles()!!.size)
        second.recordBreadcrumb("event", "different process")
        assertTrue(pending.delete())
        val report = reports(capture(root, "third")).single()
        assertEquals("first", report.getString("previousProcessSessionId"))
        assertEquals("before crash", report.getJSONArray("breadcrumbs").getJSONObject(0).getString("label"))
        assertEquals(0, File(root, "raw").listFiles()!!.size)
    }

    @Test fun signalWithoutExitHistoryRetainsNonfatalManagedException() {
        val root = temporary.newFolder()
        val first = capture(root, "first")
        record(first, false)
        val signal = File(root, "signals/first.raw").also { it.parentFile!!.mkdirs(); it.writeText("signal") }
        val second = capture(root, "second", readSignal = { file ->
            if (file == signal && file.exists()) AndroidCrashSignalEvidence(6, 0, 0, System.currentTimeMillis(), Process.myPid()) else null
        })
        assertEquals("managed stack", reports(second).single().getJSONObject("candidate").getString("stack"))
        assertFalse(signal.exists())
    }

    @Test fun reinitializationPreservesHostAssociationAndBreadcrumbs() {
        val root = temporary.newFolder()
        val first = capture(root, "first")
        first.associateHostSession("host", "config", "app")
        first.recordBreadcrumb("screen", "checkout")
        first.initialize(application, AnsightCrashCaptureOptions())
        record(first, true)
        val report = reports(capture(root, "next")).single()
        assertEquals("host", report.getString("hostId"))
        assertEquals("checkout", report.getJSONArray("breadcrumbs").getJSONObject(0).getString("label"))
    }

    @Test fun nonfatalCandidatesAndSessionHistoryAreBounded() {
        val root = temporary.newFolder()
        val options = AnsightCrashCaptureOptions(maximumPendingReports = 1)
        repeat(10) { index ->
            val current = capture(root, "process-$index", options)
            repeat(20) { record(current, false) }
        }
        assertTrue(reports(capture(root, "last", options)).isEmpty())
        assertTrue(File(root, "raw").listFiles()!!.size <= 16)
        assertTrue(File(root, "sessions").listFiles()!!.size <= 4)
    }

    @Test fun expiredCandidatesAreNotRevivedAsNewReports() {
        val root = temporary.newFolder()
        val options = AnsightCrashCaptureOptions(retentionDays = 1)
        val first = capture(root, "first", options)
        record(first, true)
        File(root, "raw").listFiles()!!.forEach { assertTrue(it.setLastModified(System.currentTimeMillis() - 172_800_000L)) }
        assertTrue(reports(capture(root, "next", options)).isEmpty())
    }

    @Test fun nativeOfflineAttachmentAcknowledgesOnlyAfterSuccessfulPersistence() {
        val root = temporary.newFolder()
        val offlineRoot = temporary.newFolder()
        val options = AnsightCrashCaptureOptions(hostHandoffEnabled = false)
        val first = capture(root, "first", options)
        val old = OfflineCaptureStore(offlineRoot, OfflineCaptureOptions(enabled = true), "app", "first")
        old.crashCapture = first
        old.setRecording(true)
        assertTrue(old.append("{}".toByteArray()))
        old.setRecording(false)
        val session = old.sessions().get().single()
        old.close()
        first.associateOfflineSession(session.sessionId, null)
        record(first, true)
        val next = capture(root, "next", options)
        OfflineCaptureStore(offlineRoot, OfflineCaptureOptions(enabled = true), "app", "next").use { store ->
            store.crashCapture = next
            val folder = File(offlineRoot, "sessions/${session.sessionId}")
            val blocked = File(folder, "diagnostics").also { it.writeText("blocked") }
            try {
                store.sessions().get()
                fail("Expected attachment persistence failure")
            } catch (_: java.util.concurrent.ExecutionException) {}
            assertEquals(1, reports(next).size)
            assertTrue(blocked.delete())
            assertEquals("unhandled_exception", store.sessions().get().single().terminationKind)
            assertTrue(reports(next).isEmpty())
            val ids = JSONObject(File(folder, "manifest.json").readText()).getJSONArray("crashReportIds")
            assertEquals(1, ids.length())
            assertTrue(File(folder, "diagnostics/crashes/${ids.getString(0)}.json").exists())
        }
    }
}
