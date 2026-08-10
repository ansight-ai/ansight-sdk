package ai.ansight.runtime

import org.json.JSONObject
import java.nio.ByteBuffer
import java.nio.ByteOrder

object SessionJpegWireProtocol {
    const val HeaderSize = 28
    private const val Version: Byte = 1
    private const val FormatJpeg: Byte = 1

    fun createFrame(
        capturedAtEpochMs: Long,
        width: Int,
        height: Int,
        quality: Int,
        jpegBytes: ByteArray,
    ): ByteArray {
        val frame = ByteArray(HeaderSize + jpegBytes.size)
        frame[0] = 'A'.toInt().toByte()
        frame[1] = 'S'.toInt().toByte()
        frame[2] = 'J'.toInt().toByte()
        frame[3] = 'P'.toInt().toByte()
        frame[4] = Version
        frame[5] = FormatJpeg
        frame[6] = quality.coerceIn(1, 100).toByte()
        frame[7] = 0
        ByteBuffer.wrap(frame, 8, 8).order(ByteOrder.LITTLE_ENDIAN).putLong(capturedAtEpochMs)
        ByteBuffer.wrap(frame, 16, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(width)
        ByteBuffer.wrap(frame, 20, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(height)
        ByteBuffer.wrap(frame, 24, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(jpegBytes.size)
        jpegBytes.copyInto(frame, HeaderSize)
        return frame
    }
}

fun PairingLiveSessionTransport.sendSessionJpegFrame(
    screenshot: CapturedScreenshot,
    quality: Int,
    capturedAtEpochMs: Long = System.currentTimeMillis(),
): OperationResult {
    return sendData(
        SessionJpegWireProtocol.createFrame(
            capturedAtEpochMs = capturedAtEpochMs,
            width = screenshot.width,
            height = screenshot.height,
            quality = quality,
            jpegBytes = screenshot.bytes,
        ),
    )
}

fun PairingLiveSessionTransport.sendSessionVisualTree(
    visualTree: JSONObject,
    capturedAtUtc: String,
): OperationResult {
    val source = visualTree.optString("source", "native")
    val payload = JSONObject()
        .put("type", "CLIENT_VISUAL_TREE")
        .put("snapshotId", "stream-${java.util.UUID.randomUUID()}")
        .put("capturedAtUtc", capturedAtUtc)
        .put("screenshotCapturedAtUtc", capturedAtUtc)
        .put("visualTreeKind", source)
        .put("visualTreeFormat", visualTree.optString("format", "ansight.native.visual-tree.v1"))
        .put("runtimePlatform", visualTree.optString("platform", "android"))
        .put("source", "sdk.sessionCapture")
        .put("maxDepth", 40)
        .put("includeProperties", true)
        .put("includeBindableProperties", false)
        .put("nodeCount", visualTree.optInt("nodeCount", 0))
        .put("truncated", visualTree.optBoolean("truncated", false))
        .put("payload", visualTree)
    return sendText(payload.toString())
}
