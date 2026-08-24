package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import android.app.Application

class ToolingProtocolTest {
    @Test
    fun guardLimitsVisibleScopes() {
        assertTrue(AnsightToolGuard.ReadOnly.canDiscover(ToolPolicy.Read))
        assertFalse(AnsightToolGuard.ReadOnly.canDiscover(ToolPolicy.Write))
        assertTrue(AnsightToolGuard.ReadWrite.canDiscover(ToolPolicy.Write))
        assertFalse(AnsightToolGuard.ReadWrite.canDiscover(ToolPolicy.Critical))
        assertTrue(AnsightToolGuard.FullAccess.canDiscover(ToolPolicy.Critical))
        assertFalse(AnsightToolGuard.Disabled.canDiscover(ToolPolicy.Read))
    }

    @Test
    fun schemaSerializesObjectProperties() {
        val schema = ToolSchema.obj(
            properties = mapOf("path" to ToolSchema.string()),
            required = listOf("path"),
        ).toJson()

        assertEquals("object", schema.getString("type"))
        assertTrue(schema.getJSONObject("properties").has("path"))
        assertEquals("path", schema.getJSONArray("required").getString(0))
    }

    @Test
    fun binaryTransferFrameUsesAsftHeader() {
        val frame = PairingFileTransferWireProtocol.createFrame(
            transferId = "0123456789abcdef0123456789abcdef",
            frameType = FileTransferFrameType.Chunk,
            sequence = 7,
            offsetBytes = 11,
            payload = byteArrayOf(1, 2, 3),
        )

        assertEquals('A'.code.toByte(), frame[0])
        assertEquals('S'.code.toByte(), frame[1])
        assertEquals('F'.code.toByte(), frame[2])
        assertEquals('T'.code.toByte(), frame[3])
        assertEquals(1.toByte(), frame[4])
        assertEquals(FileTransferFrameType.Chunk.code, frame[5])
        assertEquals(PairingFileTransferWireProtocol.HeaderSize + 3, frame.size)
    }

    @Test
    fun artifactQueryToolReturnsProviderAndDefinitions() {
        val tool = AndroidArtifactTools.create { listOf(TestArtifactProvider()) }
            .first { it.definition.id == AndroidArtifactToolIds.Query }

        val result = tool.execute(
            emptyMap(),
            AndroidToolExecutionContext(
                application = Application(),
                transport = null,
                sessionId = "session-1",
                requestId = "request-1",
                options = AnsightOptions(),
            ),
        )

        assertTrue(result.success)
        val payload = result.payload!!
        assertEquals(1, payload.getInt("providerCount"))
        assertEquals(1, payload.getInt("artifactCount"))
        assertEquals("app.report", payload.getJSONArray("providers").getJSONObject(0).getString("id"))
        assertEquals("current", payload.getJSONArray("artifacts").getJSONObject(0).getString("id"))
    }

    @Test
    fun artifactRequestToolRequiresLiveTransport() {
        val tool = AndroidArtifactTools.create { listOf(TestArtifactProvider()) }
            .first { it.definition.id == AndroidArtifactToolIds.Request }

        val result = tool.execute(
            mapOf("providerId" to "app.report", "artifactId" to "current"),
            AndroidToolExecutionContext(
                application = Application(),
                transport = null,
                sessionId = "session-1",
                requestId = "request-1",
                options = AnsightOptions(),
            ),
        )

        assertFalse(result.success)
        assertEquals("artifact_transfer_unavailable", result.errorCode)
    }

    @Test
    fun runtimeFacadeExposesDotNetStyleEventAndLifecycleApis() {
        val runtimeClass = Class.forName("ai.ansight.runtime.Runtime", false, javaClass.classLoader)
        val methods = runtimeClass.methods.map { method ->
            method.name to method.parameterTypes.map { it.simpleName }
        }.toSet()

        assertTrue(methods.contains("Event" to listOf("String")))
        assertTrue(methods.contains("Event" to listOf("String", "AnsightEventType")))
        assertTrue(methods.contains("Event" to listOf("String", "int")))
        assertTrue(methods.contains("Event" to listOf("String", "AnsightEventType", "int", "String")))
        assertTrue(methods.contains("ScreenViewed" to listOf("String")))
        assertTrue(methods.contains("SetAppLifecycleState" to listOf("AppLifecycleState")))
    }

    private class TestArtifactProvider : AndroidArtifactProvider {
        override val descriptor = AndroidArtifactProviderDescriptor(
            id = "app.report",
            name = "App Report",
            description = "Test report provider.",
        )

        override fun query(context: AndroidArtifactQueryContext): List<AndroidArtifactDefinition> = listOf(
            AndroidArtifactDefinition(
                id = "current",
                name = "Current Report",
                description = "Current report.",
                kind = "text",
                category = "diagnostics",
                mimeType = "text/plain",
                fileName = "report.txt",
                estimatedSizeBytes = 5,
                tags = listOf("debug"),
            ),
        )

        override fun create(request: AndroidArtifactRequest): AndroidArtifactResult {
            val bytes = "hello".toByteArray()
            return AndroidArtifactResult(
                metadata = AndroidArtifactMetadata(
                    providerId = descriptor.id,
                    artifactId = request.artifactId,
                    name = "Current Report",
                    kind = "text",
                    mimeType = "text/plain",
                    fileName = "report.txt",
                    sizeBytes = bytes.size.toLong(),
                ),
                bytes = bytes,
            )
        }
    }
}
