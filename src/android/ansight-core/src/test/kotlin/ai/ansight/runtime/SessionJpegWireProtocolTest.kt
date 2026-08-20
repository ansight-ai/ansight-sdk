package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Test

class SessionJpegWireProtocolTest {
    @Test
    fun keyboardPresenceUsesReservedHeaderFlags() {
        val unknown = createFrame(keyboardPresent = null)
        val absent = createFrame(keyboardPresent = false)
        val present = createFrame(keyboardPresent = true)

        assertEquals(0, unknown[7].toInt())
        assertEquals(SessionJpegWireProtocol.KeyboardPresenceKnownFlag, absent[7].toInt())
        assertEquals(
            SessionJpegWireProtocol.KeyboardPresenceKnownFlag or SessionJpegWireProtocol.KeyboardPresentFlag,
            present[7].toInt(),
        )
    }

    private fun createFrame(keyboardPresent: Boolean?): ByteArray =
        SessionJpegWireProtocol.createFrame(
            capturedAtEpochMs = 1_234,
            width = 1,
            height = 1,
            quality = 60,
            jpegBytes = byteArrayOf(),
            keyboardPresent = keyboardPresent,
        )
}
