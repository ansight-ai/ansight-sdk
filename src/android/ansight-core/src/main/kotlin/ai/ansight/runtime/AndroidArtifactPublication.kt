package ai.ansight.runtime

import org.json.JSONObject

internal object AndroidArtifactPublication {
    const val MaximumSizeBytes = 512L * 1024 * 1024

    fun send(metadata: JSONObject, bytes: ByteArray,
        control: (String, JSONObject) -> OperationResult,
        binary: (ByteArray) -> OperationResult): OperationResult {
        val transferId = PairingFileTransferWireProtocol.newTransferId()
        val identity = JSONObject().put("transferId", transferId)
        var begun = false
        var committed = false
        try {
            if (Thread.currentThread().isInterrupted) throw InterruptedException("Artifact publication cancelled.")
            for (key in listOf("artifactId", "providerId", "name", "kind", "mimeType", "fileName")) {
                require(metadata.opt(key) is String && metadata.getString(key).isNotBlank()) { "Artifact metadata requires '$key'." }
            }
            require(bytes.size.toLong() <= MaximumSizeBytes) { "Published artifacts must be at most 512 MiB." }
            if (metadata.has("sizeBytes") && !metadata.isNull("sizeBytes"))
                require(metadata.getLong("sizeBytes") == bytes.size.toLong()) { "Artifact metadata size does not match its payload." }
            val normalized = JSONObject(metadata.toString()).put("sizeBytes", bytes.size)
            if (!normalized.has("createdAtUtc")) normalized.put("createdAtUtc", AnsightClock.isoNow())
            val ready = control("artifact.publish.begin", JSONObject()
                .put("schema", "ansight.artifact.publish.v1").put("transferId", transferId)
                .put("wireProtocol", PairingFileTransferWireProtocol.ProtocolName).put("artifact", normalized))
            if (!ready.success) return ready
            begun = true
            var offset = 0
            var sequence = 0
            while (offset < bytes.size) {
                if (Thread.currentThread().isInterrupted) throw InterruptedException("Artifact publication cancelled.")
                val end = (offset + 64 * 1024).coerceAtMost(bytes.size)
                val sent = binary(PairingFileTransferWireProtocol.createFrame(transferId,
                    FileTransferFrameType.Chunk, sequence++, offset.toLong(), bytes.copyOfRange(offset, end)))
                if (!sent.success) return sent
                offset = end
            }
            if (Thread.currentThread().isInterrupted) throw InterruptedException("Artifact publication cancelled.")
            val complete = binary(PairingFileTransferWireProtocol.createFrame(transferId,
                FileTransferFrameType.Complete, sequence, offset.toLong(), ByteArray(0)))
            if (!complete.success) return complete
            if (Thread.currentThread().isInterrupted) throw InterruptedException("Artifact publication cancelled.")
            val result = control("artifact.publish.commit", identity)
            committed = result.success
            return result
        } catch (error: Exception) {
            if (error is InterruptedException) Thread.currentThread().interrupt()
            return OperationResult.failure("Could not publish artifact: ${error.message}")
        } finally {
            if (begun && !committed) runCatching { control("artifact.publish.abort", identity) }
        }
    }
}
