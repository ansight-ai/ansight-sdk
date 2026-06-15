package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ToolingProtocolTest {
    @Test
    fun guardLimitsVisibleScopes() {
        assertTrue(AnsightToolGuard.ReadOnly.canDiscover(ToolScope.Read))
        assertFalse(AnsightToolGuard.ReadOnly.canDiscover(ToolScope.Write))
        assertTrue(AnsightToolGuard.Full.canDiscover(ToolScope.Delete))
        assertFalse(AnsightToolGuard.Disabled.canDiscover(ToolScope.Read))
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
}
