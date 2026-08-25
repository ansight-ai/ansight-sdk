package ai.ansight.runtime

import android.app.Application
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Base64
import java.util.zip.GZIPInputStream

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
    fun toolDefinitionSerializesCompactV3Metadata() {
        val definition = ToolDefinition(
            id = "map.capture",
            name = "Capture Map",
            description = "Capture the current map.",
            category = "map",
            policy = ToolPolicy.Read,
            keywords = "",
            argumentsSchema = ToolSchema.obj(
                properties = mapOf("quality" to ToolSchema.integer()),
            ),
            resultSchema = ToolSchema.obj(),
            prerequisiteToolIds = listOf("route.open"),
        ).toJson()

        assertEquals("ansight.tool-catalog.v3", ToolProtocol.CatalogSchema)
        assertFalse(definition.has("keywords"))
        assertFalse(definition.getJSONObject("argumentsSchema").has("additionalProperties"))
        assertEquals("route.open", definition.getJSONArray("prerequisiteToolIds").getString(0))
        assertFalse(ToolAvailability.Available.toJson().has("evaluatedAtUtc"))
        assertFalse(ToolAvailability.Available.toJson().has("retryable"))
    }

    @Test
    fun toolProtocolPayloadEncodingRoundTripsLargeEnvelope() {
        val envelope = JSONObject()
            .put("type", ToolProtocol.CatalogType)
            .put("payload", JSONObject().put("description", "x".repeat(64 * 1024)))

        val encodedEnvelope = ToolProtocolPayloadEncoding.encodeEnvelopeIfBeneficial(envelope)
        val encodedPayload = encodedEnvelope.getJSONObject("payload")
        assertEquals("gzip-base64-json", encodedPayload.getString("\$ansightEncoding"))
        assertTrue(encodedPayload.getInt("originalByteCount") > encodedPayload.getInt("compressedByteCount"))

        val standardDecodedPayload = GZIPInputStream(
            Base64.getDecoder().decode(encodedPayload.getString("data")).inputStream(),
        ).bufferedReader().use { reader -> JSONObject(reader.readText()) }
        assertEquals("x".repeat(64 * 1024), standardDecodedPayload.getString("description"))

        val decodedPayload = ToolProtocolPayloadEncoding.decodeIfNeeded(encodedPayload)!!
        assertEquals("x".repeat(64 * 1024), decodedPayload.getString("description"))
    }

    @Test
    fun toolProtocolPayloadEncodingLeavesSmallEnvelopeUnchanged() {
        val envelope = JSONObject()
            .put("type", ToolProtocol.CatalogType)
            .put("payload", JSONObject().put("count", 1))

        val encodedEnvelope = ToolProtocolPayloadEncoding.encodeEnvelopeIfBeneficial(envelope)

        assertFalse(encodedEnvelope.getJSONObject("payload").has("\$ansightEncoding"))
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
