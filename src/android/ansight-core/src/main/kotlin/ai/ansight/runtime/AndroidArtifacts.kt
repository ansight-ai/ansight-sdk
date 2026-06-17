package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject

object AndroidArtifactToolIds {
    const val Query = "artifacts.query"
    const val Request = "artifacts.request"
}

data class AndroidArtifactProviderDescriptor(
    val id: String,
    val name: String,
    val description: String? = null,
    val category: String = "app",
) {
    fun validated(): AndroidArtifactProviderDescriptor {
        require(id.isNotBlank()) { "Artifact provider id must not be blank." }
        require(name.isNotBlank()) { "Artifact provider name must not be blank." }
        return copy(
            id = id.trim(),
            name = name.trim(),
            description = description?.trim()?.ifBlank { null },
            category = category.trim().ifBlank { "app" },
        )
    }

    fun toJson(error: String? = null): JSONObject = JSONObject()
        .put("id", id)
        .put("name", name)
        .putNullable("description", description)
        .put("category", category)
        .putNullable("error", error)
}

data class AndroidArtifactDefinition(
    val id: String,
    val name: String,
    val description: String,
    val kind: String,
    val category: String,
    val mimeType: String,
    val fileName: String,
    val estimatedSizeBytes: Long? = null,
    val tags: List<String> = emptyList(),
    val metadata: Map<String, String> = emptyMap(),
) {
    fun validated(): AndroidArtifactDefinition {
        require(id.isNotBlank()) { "Artifact id must not be blank." }
        require(name.isNotBlank()) { "Artifact name must not be blank." }
        require(kind.isNotBlank()) { "Artifact kind must not be blank." }
        require(category.isNotBlank()) { "Artifact category must not be blank." }
        require(mimeType.isNotBlank()) { "Artifact MIME type must not be blank." }
        require(fileName.isNotBlank()) { "Artifact file name must not be blank." }
        return copy(
            id = id.trim(),
            name = name.trim(),
            description = description.trim(),
            kind = kind.trim(),
            category = category.trim(),
            mimeType = mimeType.trim(),
            fileName = fileName.trim(),
            tags = tags.mapNotNull { it.trim().ifBlank { null } },
            metadata = metadata.mapNotNull { entry ->
                val key = entry.key.trim()
                if (key.isBlank()) null else key to entry.value.trim()
            }.toMap(),
        )
    }

    fun toJson(providerId: String): JSONObject = JSONObject()
        .put("providerId", providerId)
        .put("id", id)
        .put("name", name)
        .put("description", description)
        .put("kind", kind)
        .put("category", category)
        .put("content", JSONObject()
            .put("supportedMimeTypes", JSONArray(listOf(mimeType)))
            .put("defaultMimeType", mimeType)
            .put("suggestedFileName", fileName)
            .putNullable("estimatedSizeBytes", estimatedSizeBytes))
        .put("tags", JSONArray(tags))
        .put("metadata", JSONObject(metadata))
}

data class AndroidArtifactMetadata(
    val providerId: String,
    val artifactId: String,
    val name: String,
    val kind: String,
    val mimeType: String,
    val fileName: String,
    val sizeBytes: Long,
    val description: String = "",
    val tags: List<String> = emptyList(),
    val metadata: Map<String, String> = emptyMap(),
) {
    fun toJson(): JSONObject = JSONObject()
        .put("providerId", providerId)
        .put("artifactId", artifactId)
        .put("name", name)
        .put("kind", kind)
        .put("description", description)
        .put("mimeType", mimeType)
        .put("fileName", fileName)
        .put("sizeBytes", sizeBytes)
        .put("createdAtUtc", AnsightClock.isoNow())
        .put("tags", JSONArray(tags))
        .put("metadata", JSONObject(metadata))
}

data class AndroidArtifactResult(
    val metadata: AndroidArtifactMetadata,
    val bytes: ByteArray,
) {
    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other !is AndroidArtifactResult) return false
        return metadata == other.metadata && bytes.contentEquals(other.bytes)
    }

    override fun hashCode(): Int {
        return 31 * metadata.hashCode() + bytes.contentHashCode()
    }
}

data class AndroidArtifactQueryContext(
    val requestId: String?,
    val sessionId: String?,
    val capturedAtUtc: String,
)

data class AndroidArtifactRequest(
    val providerId: String,
    val artifactId: String,
    val arguments: Map<String, String>,
    val requestId: String?,
    val sessionId: String?,
    val requestedAtUtc: String,
)

interface AndroidArtifactProvider {
    val descriptor: AndroidArtifactProviderDescriptor
    fun query(context: AndroidArtifactQueryContext): List<AndroidArtifactDefinition>
    fun create(request: AndroidArtifactRequest): AndroidArtifactResult
}

object AndroidArtifactTools {
    private const val DefaultChunkBytes = 64 * 1024

    fun create(providers: () -> List<AndroidArtifactProvider>): List<AndroidTool> = listOf(
        androidSimpleTool(
            id = AndroidArtifactToolIds.Query,
            name = "Query Artifacts",
            description = "Queries app-provided artifact providers and currently requestable artifact definitions.",
            category = "artifacts",
            scope = ToolScope.Read,
            keywords = "artifact artifacts query catalog provider export snapshot",
            handler = { args, context ->
            val capturedAtUtc = AnsightClock.isoNow()
            val providerFilter = args["providerId"]?.trim()?.ifBlank { null }
            val categoryFilter = args["category"]?.trim()?.ifBlank { null }
            val kindFilter = args["kind"]?.trim()?.ifBlank { null }
            val tagFilter = args["tag"]?.trim()?.ifBlank { null }
            val providerArray = JSONArray()
            val artifactArray = JSONArray()

            providers().forEach { provider ->
                val descriptor = provider.descriptor.validated()
                if (providerFilter != null && !descriptor.id.equals(providerFilter, ignoreCase = true)) {
                    return@forEach
                }
                try {
                    providerArray.put(descriptor.toJson())
                    provider.query(
                        AndroidArtifactQueryContext(
                            requestId = context.requestId,
                            sessionId = context.sessionId,
                            capturedAtUtc = capturedAtUtc,
                        ),
                    ).map { it.validated() }
                        .filter { definition -> definition.matches(categoryFilter, kindFilter, tagFilter) }
                        .forEach { definition -> artifactArray.put(definition.toJson(descriptor.id)) }
                } catch (ex: Exception) {
                    providerArray.put(descriptor.toJson(ex.message ?: "Artifact provider query failed."))
                }
            }

            AndroidToolResult.success(
                JSONObject()
                    .put("providers", providerArray)
                    .put("artifacts", artifactArray)
                    .put("providerCount", providerArray.length())
                    .put("artifactCount", artifactArray.length())
                    .put("capturedAtUtc", capturedAtUtc),
            )
            },
        ),
        androidSimpleTool(
            id = AndroidArtifactToolIds.Request,
            name = "Request Artifact",
            description = "Requests an app-provided artifact snapshot and streams it to the host.",
            category = "artifacts",
            scope = ToolScope.Read,
            keywords = "artifact artifacts request export snapshot binary stream",
            handler = requestHandler@{ args, context ->
            val requestId = context.requestId ?: return@requestHandler AndroidToolResult.failure(
                "Artifact requests require a live tool protocol request context.",
                "artifact_request_unavailable",
            )
            val providerId = args["providerId"]?.trim()?.ifBlank { null } ?: return@requestHandler AndroidToolResult.failure(
                "Artifact request must include 'providerId'.",
                "artifact_request_missing_provider_id",
            )
            val artifactId = args["artifactId"]?.trim()?.ifBlank { null } ?: return@requestHandler AndroidToolResult.failure(
                "Artifact request must include 'artifactId'.",
                "artifact_request_missing_artifact_id",
            )
            val provider = providers().firstOrNull { it.descriptor.id.equals(providerId, ignoreCase = true) }
                ?: return@requestHandler AndroidToolResult.failure(
                    "Artifact provider '$providerId' is not registered.",
                    "artifact_provider_not_found",
                )
            val transport = context.transport ?: return@requestHandler AndroidToolResult.failure(
                "Artifact requests require a live pairing session.",
                "artifact_transfer_unavailable",
            )

            val requestedAtUtc = AnsightClock.isoNow()
            val requestArguments = args.filterKeys {
                it !in setOf("providerId", "artifactId", "downloadId", "chunkBytes")
            }
            val result = provider.create(
                AndroidArtifactRequest(
                    providerId = providerId,
                    artifactId = artifactId,
                    arguments = requestArguments,
                    requestId = requestId,
                    sessionId = context.sessionId,
                    requestedAtUtc = requestedAtUtc,
                ),
            )
            val bytes = result.bytes
            val metadata = result.metadata
            val chunkBytes = args["chunkBytes"]?.toIntOrNull()?.coerceIn(1024, 512 * 1024) ?: DefaultChunkBytes
            val transferId = PairingFileTransferWireProtocol.newTransferId()
            val downloadId = args["downloadId"]?.trim()?.ifBlank { null } ?: requestId
            val descriptor = BinaryTransferDescriptor(
                transferId = transferId,
                downloadId = downloadId,
                fileName = metadata.fileName,
                mimeType = metadata.mimeType,
                sizeBytes = bytes.size.toLong(),
                chunkBytes = chunkBytes,
            ).toJson()

            Thread {
                transport.sendBinaryTransfer(transferId, bytes, chunkBytes)
            }.apply {
                name = "AnsightAndroidArtifactTransfer"
                isDaemon = true
                start()
            }

            AndroidToolResult.success(
                JSONObject()
                    .put("artifact", metadata.toJson())
                    .put("downloadId", downloadId)
                    .put("transferId", transferId)
                    .put("deliveryMode", "websocket_binary")
                    .put("wireProtocol", PairingFileTransferWireProtocol.ProtocolName)
                    .put("status", descriptor.optString("status", "queued"))
                    .put("chunkBytes", chunkBytes)
                    .put("capturedAtUtc", requestedAtUtc),
            )
            },
        ),
    )

    private fun AndroidArtifactDefinition.matches(category: String?, kind: String?, tag: String?): Boolean =
        (category == null || this.category.equals(category, ignoreCase = true)) &&
            (kind == null || this.kind.equals(kind, ignoreCase = true)) &&
            (tag == null || this.tags.any { it.equals(tag, ignoreCase = true) })
}
