package ai.ansight.runtime

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.charset.StandardCharsets
import java.util.UUID

enum class FileTransferFrameType(val code: Byte) {
    Chunk(1),
    Complete(2),
    Error(3),
}

object PairingFileTransferWireProtocol {
    const val ProtocolName = "ansight.file-transfer.v1"
    const val HeaderSize = 56
    private const val Version: Byte = 1

    fun createFrame(
        transferId: String,
        frameType: FileTransferFrameType,
        sequence: Int,
        offsetBytes: Long,
        payload: ByteArray,
    ): ByteArray {
        val normalizedTransferId = transferId.replace("-", "").padEnd(32, '0').take(32)
        val frame = ByteArray(HeaderSize + payload.size)
        frame[0] = 'A'.code.toByte()
        frame[1] = 'S'.code.toByte()
        frame[2] = 'F'.code.toByte()
        frame[3] = 'T'.code.toByte()
        frame[4] = Version
        frame[5] = frameType.code
        frame[6] = 0
        frame[7] = 0
        normalizedTransferId.toByteArray(StandardCharsets.US_ASCII).copyInto(frame, 8, 0, 32)
        ByteBuffer.wrap(frame, 40, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(sequence)
        ByteBuffer.wrap(frame, 44, 8).order(ByteOrder.LITTLE_ENDIAN).putLong(offsetBytes)
        ByteBuffer.wrap(frame, 52, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(payload.size)
        payload.copyInto(frame, HeaderSize)
        return frame
    }

    fun newTransferId(): String = UUID.randomUUID().toString().replace("-", "")
}

data class BinaryTransferDescriptor(
    val transferId: String,
    val downloadId: String,
    val fileName: String,
    val mimeType: String,
    val sizeBytes: Long,
    val chunkBytes: Int,
    val status: String = "queued",
    val wireProtocol: String = PairingFileTransferWireProtocol.ProtocolName,
) {
    fun toJson(): org.json.JSONObject = org.json.JSONObject()
        .put("deliveryMode", "websocket_binary")
        .put("wireProtocol", wireProtocol)
        .put("transferId", transferId)
        .put("downloadId", downloadId)
        .put("fileName", fileName)
        .put("mimeType", mimeType)
        .put("sizeBytes", sizeBytes)
        .put("chunkBytes", chunkBytes)
        .put("status", status)
}

fun PairingLiveSessionTransport.sendBinaryTransfer(
    transferId: String,
    bytes: ByteArray,
    chunkBytes: Int = 64 * 1024,
): OperationResult {
    val chunkSize = chunkBytes.coerceIn(1024, 1024 * 1024)
    var offset = 0
    var sequence = 0
    while (offset < bytes.size) {
        val end = (offset + chunkSize).coerceAtMost(bytes.size)
        val payload = bytes.copyOfRange(offset, end)
        val result = sendData(
            PairingFileTransferWireProtocol.createFrame(
                transferId = transferId,
                frameType = FileTransferFrameType.Chunk,
                sequence = sequence,
                offsetBytes = offset.toLong(),
                payload = payload,
            ),
        )
        if (!result.success) {
            return result
        }
        offset = end
        sequence += 1
    }

    val result = sendData(
        PairingFileTransferWireProtocol.createFrame(
            transferId = transferId,
            frameType = FileTransferFrameType.Complete,
            sequence = sequence,
            offsetBytes = bytes.size.toLong(),
            payload = ByteArray(0),
        ),
    )
    return if (result.success) OperationResult.success("Binary transfer $transferId complete.") else result
}
