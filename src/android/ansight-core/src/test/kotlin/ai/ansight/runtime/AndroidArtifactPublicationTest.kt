package ai.ansight.runtime

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class AndroidArtifactPublicationTest {
    private fun metadata() = JSONObject().put("providerId", "scene").put("artifactId", "model")
        .put("name", "Model").put("kind", "model").put("mimeType", "model/gltf-binary").put("fileName", "model.glb")

    @Test fun sendsChunksThenWaitsForHostCommit() {
        val actions = mutableListOf<String>()
        val frames = mutableListOf<ByteArray>()
        val bytes = ByteArray(65537) { (it % 251).toByte() }
        val result = AndroidArtifactPublication.send(metadata(), bytes, { action, payload ->
            actions += action
            if (action.endsWith("begin")) assertEquals(bytes.size, payload.getJSONObject("artifact").getInt("sizeBytes"))
            if (action.endsWith("commit")) assertEquals(3, frames.size)
            OperationResult.success("retained")
        }, { frame -> frames += frame; OperationResult.success("sent") })
        assertTrue(result.success)
        assertEquals(listOf("artifact.publish.begin", "artifact.publish.commit"), actions)
        assertArrayEquals(bytes, frames[0].copyOfRange(56, frames[0].size) + frames[1].copyOfRange(56, frames[1].size))
        assertEquals(2.toByte(), frames.last()[5])
    }

    @Test fun rejectedCommitAndTransportFailureAbort() {
        for (failBinary in listOf(false, true)) {
            val actions = mutableListOf<String>()
            val result = AndroidArtifactPublication.send(metadata(), byteArrayOf(1), { action, _ ->
                actions += action
                if (action.endsWith("commit")) OperationResult.failure("disk full") else OperationResult.success("ok")
            }, { if (failBinary) OperationResult.failure("disconnected") else OperationResult.success("sent") })
            assertFalse(result.success)
            assertEquals("artifact.publish.abort", actions.last())
        }
    }

    @Test fun validatesMetadataBeforeStartingAndDoesNotSendAfterRejection() {
        var frames = 0
        var controls = 0
        val invalid = AndroidArtifactPublication.send(metadata().put("sizeBytes", 100), byteArrayOf(1),
            { _, _ -> controls++; OperationResult.success("ok") }, { frames++; OperationResult.success("ok") })
        assertFalse(invalid.success)
        assertEquals(0, controls)
        val rejected = AndroidArtifactPublication.send(metadata(), byteArrayOf(1),
            { _, _ -> controls++; OperationResult.failure("unsupported host") }, { frames++; OperationResult.success("ok") })
        assertFalse(rejected.success)
        assertEquals(0, frames)
        assertEquals(1, controls)
    }

    @Test fun cancellationAfterCompleteDoesNotCommit() {
        val actions = mutableListOf<String>()
        try {
            val result = AndroidArtifactPublication.send(metadata(), byteArrayOf(), { action, _ ->
                actions += action
                OperationResult.success("ok")
            }, {
                Thread.currentThread().interrupt()
                OperationResult.success("sent")
            })
            assertFalse(result.success)
            assertEquals(listOf("artifact.publish.begin", "artifact.publish.abort"), actions)
            assertTrue(Thread.currentThread().isInterrupted)
        } finally { Thread.interrupted() }
    }
}
