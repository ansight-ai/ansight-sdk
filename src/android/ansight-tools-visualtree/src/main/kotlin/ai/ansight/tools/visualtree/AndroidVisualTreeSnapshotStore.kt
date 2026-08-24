package ai.ansight.tools.visualtree

import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult
import org.json.JSONObject
import java.util.UUID
import java.util.concurrent.atomic.AtomicLong

internal data class AndroidVisualTreeSnapshot(
    val snapshotId: String,
    val source: String,
    val revision: Long,
    val payload: JSONObject,
    val nodeIds: Set<String>,
)

internal object AndroidVisualTreeSnapshotStore {
    private const val MaximumSnapshots = 32
    private val lock = Any()
    private val nextRevision = AtomicLong()
    private val snapshots = linkedMapOf<String, AndroidVisualTreeSnapshot>()
    private val latestRevisions = mutableMapOf<String, Long>()

    fun capture(
        source: String?,
        arguments: Map<String, String>,
        context: AndroidToolExecutionContext,
    ): AndroidToolResult {
        val normalizedSource = AndroidVisualTreeProviderRegistry.normalizeSourceOrDefault(source)
        val provider = AndroidVisualTreeProviderRegistry.provider(normalizedSource)
            ?: return AndroidToolResult.failure(
                "No visual tree provider is registered for source '$normalizedSource'.",
                "visual_tree_provider_not_found",
            )
        val result = provider.getVisualTree(arguments, context)
        val payload = result.payload
        if (!result.success || payload == null) return result

        val revision = nextRevision.incrementAndGet()
        val snapshotId = "$normalizedSource:$revision:${UUID.randomUUID().toString().replace("-", "")}"
        payload
            .put("source", normalizedSource)
            .put("snapshotId", snapshotId)
            .put("revision", revision)
            .put("nodeIdentity", JSONObject()
                .put("scope", "snapshot")
                .put("source", normalizedSource)
                .put("staleAfterRevision", revision))
        val storedPayload = JSONObject(payload.toString())
        val snapshot = AndroidVisualTreeSnapshot(
            snapshotId,
            normalizedSource,
            revision,
            storedPayload,
            collectNodeIds(storedPayload.optJSONObject("root")),
        )
        synchronized(lock) {
            snapshots[snapshotId] = snapshot
            latestRevisions[normalizedSource] = revision
            while (snapshots.size > MaximumSnapshots) {
                snapshots.remove(snapshots.keys.first())
            }
        }
        return AndroidToolResult.success(payload, result.message)
    }

    fun current(snapshotId: String, source: String?): Pair<AndroidVisualTreeSnapshot?, AndroidToolResult?> {
        synchronized(lock) {
            val snapshot = snapshots[snapshotId]
                ?: return null to stale(
                    snapshotId,
                    AndroidVisualTreeProviderRegistry.normalizeSourceOrDefault(source),
                    "The referenced UI snapshot is unknown or has expired.",
                )
            val normalizedSource = source
                ?.takeIf { it.isNotBlank() }
                ?.let(AndroidVisualTreeProviderRegistry::normalizeSourceOrDefault)
                ?: snapshot.source
            if (!snapshot.source.equals(normalizedSource, ignoreCase = true)) {
                return null to stale(snapshotId, normalizedSource, "Snapshot '$snapshotId' belongs to source '${snapshot.source}'.")
            }
            val latestRevision = latestRevisions[normalizedSource]
            if (latestRevision != null && latestRevision != snapshot.revision) {
                return null to stale(
                    snapshotId,
                    normalizedSource,
                    "Snapshot '$snapshotId' was superseded by revision $latestRevision.",
                    latestRevision,
                )
            }
            return snapshot to null
        }
    }

    fun validateNode(snapshotId: String, source: String?, nodeId: String): Pair<AndroidVisualTreeSnapshot?, AndroidToolResult?> {
        val (snapshot, error) = current(snapshotId, source)
        if (snapshot == null) return null to error
        if (nodeId !in snapshot.nodeIds) {
            return null to stale(
                snapshotId,
                snapshot.source,
                "Node '$nodeId' does not belong to snapshot '$snapshotId'.",
                snapshot.revision,
                nodeId,
            )
        }
        return snapshot to null
    }

    fun reference(snapshot: AndroidVisualTreeSnapshot, nodeId: String): JSONObject = JSONObject()
        .put("source", snapshot.source)
        .put("snapshotId", snapshot.snapshotId)
        .put("revision", snapshot.revision)
        .put("nodeId", nodeId)

    private fun collectNodeIds(root: JSONObject?): Set<String> {
        val result = linkedSetOf<String>()
        fun visit(node: JSONObject) {
            node.optString("id").takeIf { it.isNotBlank() }?.let(result::add)
            val children = node.optJSONArray("children") ?: return
            for (index in 0 until children.length()) {
                children.optJSONObject(index)?.let(::visit)
            }
        }
        root?.let(::visit)
        return result
    }

    private fun stale(
        snapshotId: String,
        source: String,
        message: String,
        latestRevision: Long? = null,
        nodeId: String? = null,
    ): AndroidToolResult = AndroidToolResult.failure(
        message,
        "stale_node_reference",
        JSONObject()
            .put("source", source)
            .put("snapshotId", snapshotId)
            .put("nodeId", nodeId ?: JSONObject.NULL)
            .put("latestRevision", latestRevision ?: JSONObject.NULL)
            .put("refreshWith", VisualTreeToolIds.QueryNodes),
    )
}
