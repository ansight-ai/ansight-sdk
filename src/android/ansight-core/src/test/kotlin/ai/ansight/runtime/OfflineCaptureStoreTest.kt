package ai.ansight.runtime

import java.io.File
import java.util.concurrent.TimeUnit
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class OfflineCaptureStoreTest {
    @get:Rule val temporary = TemporaryFolder()

    private fun recordSession(store: OfflineCaptureStore) {
        store.setRecording(true)
        assertTrue(store.append("{\"metrics\":[{\"value\":1,\"channel\":1}]}".toByteArray()))
        store.setRecording(false)
        store.flush().get(5, TimeUnit.SECONDS)
        assertNull(store.lastError)
    }

    @Test fun defaultsAreOptInWithAutomaticTransferAndTenSessions() {
        val options = OfflineCaptureOptions()
        assertFalse(options.enabled)
        assertEquals(OfflineCaptureTransferMode.Automatic, options.transferMode)
        assertEquals(10, options.maximumSessions)
    }

    @Test fun keepsNewestTenWholeSessions() {
        OfflineCaptureStore(temporary.newFolder(), OfflineCaptureOptions(enabled = true), "test", "process").use { store ->
            recordSession(store)
            val first = store.sessions().get().first()
            repeat(11) { recordSession(store) }
            val retained = store.sessions().get()
            assertEquals(10, retained.size)
            assertFalse(retained.any { it.sessionId == first.sessionId })
            assertTrue(retained.all { it.records == 1L && it.stoppedAtUtc != null && it.bytes > 0 })
        }
    }

    @Test fun leaseProtectsSessionAndWrongReceiptDoesNotPurge() {
        OfflineCaptureStore(temporary.newFolder(), OfflineCaptureOptions(enabled = true, maximumSessions = 2), "test", "process").use { store ->
            recordSession(store)
            val first = store.sessions().get().first()
            val lease = store.acquire(first.sessionId).get()
            repeat(3) { recordSession(store) }
            assertTrue(store.sessions().get().any { it.sessionId == first.sessionId })
            val hash = "a".repeat(64)
            try {
                store.acknowledge(lease, hash, "b".repeat(64)).get()
                fail("Mismatched receipt accepted")
            } catch (expected: java.util.concurrent.ExecutionException) {
                assertTrue(expected.cause is IllegalArgumentException)
            }
            assertTrue(lease.directory.exists())
            store.acknowledge(lease, hash, hash).get()
            assertFalse(lease.directory.exists())
        }
    }

    @Test fun oversizedAdmissionDoesNotBlockFlush() {
        OfflineCaptureStore(temporary.newFolder(), OfflineCaptureOptions(enabled = true, maximumQueuedBytes = 8), "test", "process").use { store ->
            assertFalse(store.append("{}".toByteArray()))
            store.setRecording(true)
            assertFalse(store.append(ByteArray(20)))
            assertTrue(store.append("{}".toByteArray()))
            store.setRecording(false)
            store.flush().get(5, TimeUnit.SECONDS)
            val session = store.sessions().get().first()
            assertEquals(1L, session.records)
            assertEquals(1L, session.droppedRecords)
        }
    }

    @Test fun recoverySealsInterruptedSessionAndRecountsPayload() {
        val root = temporary.newFolder()
        val first = OfflineCaptureStore(root, OfflineCaptureOptions(enabled = true), "test", "old").use { store ->
            recordSession(store)
            store.sessions().get().first()
        }
        val manifest = File(root, "sessions/${first.sessionId}/manifest.json")
        val json = JSONObject(manifest.readText()).apply {
            remove("stoppedAtUtc")
            remove("terminationKind")
            put("bytes", 0)
        }
        manifest.writeText(json.toString())
        OfflineCaptureStore(root, OfflineCaptureOptions(enabled = true), "test", "new").use { store ->
            store.setRecording(true)
            store.flush().get()
            val recovered = store.sessions().get().first { it.sessionId == first.sessionId }
            assertEquals("interrupted", recovered.terminationKind)
            assertEquals(first.bytes, recovered.bytes)
            store.setRecording(false)
            store.flush().get()
        }
    }

    @Test fun byteBudgetEvictsWholeSessions() {
        val options = OfflineCaptureOptions(enabled = true, maximumSessionBytes = 10, maximumRetainedBytes = 15)
        OfflineCaptureStore(temporary.newFolder(), options, "test", "process").use { store ->
            repeat(3) {
                store.setRecording(true)
                assertTrue(store.append("{\"a\":1}".toByteArray()))
                store.setRecording(false)
                store.flush().get()
            }
            val sessions = store.sessions().get()
            assertEquals(1, sessions.size)
            assertEquals(8L, sessions.first().bytes)
        }
    }

    @Test fun archiveIsReadableAndStableAcrossRetries() {
        val root = temporary.newFolder()
        OfflineCaptureStore(root, OfflineCaptureOptions(enabled = true), "test", "process").use { store ->
            recordSession(store)
            val lease = store.acquire(store.sessions().get().first().sessionId).get()
            val first = OfflineCaptureArchive.write(lease, File(root, "first.zip"))
            val second = OfflineCaptureArchive.write(lease, File(root, "second.zip"))
            assertEquals(first.sha256, second.sha256)
            assertEquals(first.byteCount, second.byteCount)
            java.util.zip.ZipFile(first.path).use { zip ->
                assertEquals("{\"metrics\":[{\"value\":1,\"channel\":1}]}\n",
                    zip.getInputStream(zip.getEntry("records.jsonl")).reader().readText())
                val manifest = JSONObject(zip.getInputStream(zip.getEntry("manifest.json")).reader().readText())
                assertEquals(lease.sessionId, manifest.getString("sessionId"))
            }
            store.release(lease).get()
        }
    }

    @Test fun retentionNeverDeletesAnActiveLeaseWhenThereIsNoRoom() {
        OfflineCaptureStore(temporary.newFolder(), OfflineCaptureOptions(enabled = true, maximumSessions = 1), "test", "process").use { store ->
            recordSession(store)
            val lease = store.acquire(store.sessions().get().first().sessionId).get()
            store.setRecording(true)
            try { store.flush().get(); fail("Blocked retention was reported as success") }
            catch (expected: java.util.concurrent.ExecutionException) { assertTrue(expected.cause is java.io.IOException) }
            assertTrue(lease.directory.exists())
            store.setRecording(false)
            store.release(lease).get()
        }
    }

    @Test fun corruptManifestFailsVisiblyAndPreservesEvidence() {
        val root = temporary.newFolder()
        val session = File(root, "sessions/${java.util.UUID.randomUUID()}")
        assertTrue(session.mkdirs())
        File(session, "manifest.json").writeText("corrupt")
        OfflineCaptureStore(root, OfflineCaptureOptions(enabled = true), "test", "process").use { store ->
            store.setRecording(true)
            try { store.flush().get(); fail("Corrupt storage was reported as success") }
            catch (expected: java.util.concurrent.ExecutionException) { assertTrue(expected.cause is java.io.IOException) }
            assertTrue(session.exists())
            store.setRecording(false)
        }
    }

    @Test fun attachmentAndIndexAreAdmittedTogether() {
        val root = temporary.newFolder()
        OfflineCaptureStore(root, OfflineCaptureOptions(enabled = true, maximumQueuedBytes = 1024), "test", "process").use { store ->
            store.setRecording(true)
            assertFalse(store.appendAttachment(ByteArray(2048), "jpg") { "{}".toByteArray() })
            assertTrue(store.appendAttachment(byteArrayOf(1, 2, 3), "jpg") { path -> JSONObject().put("path", path).toString().toByteArray() })
            store.setRecording(false)
            store.flush().get()
            val session = store.sessions().get().first()
            assertEquals(1L, session.records)
            val directory = File(root, "sessions/${session.sessionId}")
            val index = JSONObject(File(directory, "records.jsonl").readText())
            assertArrayEquals(byteArrayOf(1, 2, 3), File(directory, index.getString("path")).readBytes())
            assertEquals(1, File(directory, "attachments").listFiles()!!.size)
        }
    }
}
