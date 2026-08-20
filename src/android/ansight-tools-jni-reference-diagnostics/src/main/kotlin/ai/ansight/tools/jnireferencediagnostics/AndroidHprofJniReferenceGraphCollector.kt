package ai.ansight.tools.jnireferencediagnostics

import android.app.Application
import android.os.Debug
import ai.ansight.runtime.AnsightClock
import org.json.JSONArray
import org.json.JSONObject
import shark.GcRoot
import shark.HeapGraph
import shark.HeapObject
import shark.HprofHeapGraph
import java.io.File
import java.util.ArrayDeque
import java.util.concurrent.TimeUnit

internal data class JniReferenceGraphLimits(
    val maximumNodes: Int,
    val maximumEdges: Int,
    val maximumDepth: Int,
)

internal fun interface JniReferenceGraphCollector {
    fun capture(application: Application, limits: JniReferenceGraphLimits): JSONObject
}

internal object AndroidHprofJniReferenceGraphCollector : JniReferenceGraphCollector {
    @Synchronized
    override fun capture(application: Application, limits: JniReferenceGraphLimits): JSONObject {
        val startedAtNanos = System.nanoTime()
        val capturedAtUtc = AnsightClock.isoNow()
        val heapDump = File.createTempFile("ansight-jni-references-", ".hprof", application.cacheDir)
        return try {
            Debug.dumpHprofData(heapDump.absolutePath)
            val heapDumpBytes = heapDump.length()
            with(HprofHeapGraph) { heapDump.openHeapGraph() }.use { graph ->
                buildGraph(
                    graph = graph,
                    limits = limits,
                    capturedAtUtc = capturedAtUtc,
                    heapDumpBytes = heapDumpBytes,
                    startedAtNanos = startedAtNanos,
                )
            }
        } finally {
            runCatching { heapDump.delete() }
        }
    }

    private fun buildGraph(
        graph: HeapGraph,
        limits: JniReferenceGraphLimits,
        capturedAtUtc: String,
        heapDumpBytes: Long,
        startedAtNanos: Long,
    ): JSONObject {
        val jniRoots = graph.gcRoots.filter {
            it is GcRoot.JniGlobal || it is GcRoot.JniLocal || it is GcRoot.JniMonitor
        }
        val globalRootCount = jniRoots.count { it is GcRoot.JniGlobal }
        val localRootCount = jniRoots.count { it is GcRoot.JniLocal }
        val monitorRootCount = jniRoots.count { it is GcRoot.JniMonitor }
        val nodesByHeapId = linkedMapOf<Long, CapturedNode>()
        val pending = ArrayDeque<CapturedNode>()
        val roots = JSONArray()
        val nodes = JSONArray()
        val edges = JSONArray()
        val truncationReasons = linkedSetOf<String>()

        jniRoots.forEachIndexed { index, root ->
            val node = findOrAddNode(
                graph = graph,
                heapObjectId = root.id,
                depth = 0,
                limits = limits,
                nodesByHeapId = nodesByHeapId,
                pending = pending,
                nodes = nodes,
            )
            if (node == null && nodesByHeapId.size >= limits.maximumNodes) {
                truncationReasons += "max_nodes"
            }
            if (roots.length() < limits.maximumNodes) {
                roots.put(rootJson(root, index, node?.id))
            } else {
                truncationReasons += "max_roots"
            }
        }

        while (pending.isNotEmpty()) {
            val source = pending.removeFirst()
            if (source.depth >= limits.maximumDepth) {
                truncationReasons += "max_depth"
                continue
            }

            for (reference in outgoingReferences(source.heapObject)) {
                if (edges.length() >= limits.maximumEdges) {
                    truncationReasons += "max_edges"
                    break
                }
                if (reference.targetHeapObjectId == 0L || !graph.objectExists(reference.targetHeapObjectId)) {
                    continue
                }
                val target = findOrAddNode(
                    graph = graph,
                    heapObjectId = reference.targetHeapObjectId,
                    depth = source.depth + 1,
                    limits = limits,
                    nodesByHeapId = nodesByHeapId,
                    pending = pending,
                    nodes = nodes,
                )
                if (target == null) {
                    truncationReasons += "max_nodes"
                    continue
                }
                edges.put(
                    JSONObject()
                        .put("from", source.id)
                        .put("to", target.id)
                        .put("kind", reference.kind)
                        .put("label", reference.label)
                        .put("declaringClass", reference.declaringClass ?: JSONObject.NULL),
                )
            }
        }

        return JSONObject()
            .put("schemaVersion", "ansight.jni-reference-graph.v1")
            .put("capturedAtUtc", capturedAtUtc)
            .put("provider", "android_hprof_shark")
            .put("jniRootCount", jniRoots.size)
            .put("jniGlobalRootCount", globalRootCount)
            .put("jniLocalRootCount", localRootCount)
            .put("jniMonitorRootCount", monitorRootCount)
            .put("heapObjectCount", graph.objectCount)
            .put("heapDumpBytes", heapDumpBytes)
            .put(
                "captureDurationMilliseconds",
                TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - startedAtNanos),
            )
            .put(
                "limits",
                JSONObject()
                    .put("maxNodes", limits.maximumNodes)
                    .put("maxEdges", limits.maximumEdges)
                    .put("maxDepth", limits.maximumDepth),
            )
            .put("roots", roots)
            .put("nodes", nodes)
            .put("edges", edges)
            .put("truncated", truncationReasons.isNotEmpty())
            .put("truncationReasons", JSONArray(truncationReasons.toList()))
    }

    private fun findOrAddNode(
        graph: HeapGraph,
        heapObjectId: Long,
        depth: Int,
        limits: JniReferenceGraphLimits,
        nodesByHeapId: MutableMap<Long, CapturedNode>,
        pending: ArrayDeque<CapturedNode>,
        nodes: JSONArray,
    ): CapturedNode? {
        nodesByHeapId[heapObjectId]?.let { return it }
        if (nodesByHeapId.size >= limits.maximumNodes) return null
        val heapObject = graph.findObjectByIdOrNull(heapObjectId) ?: return null
        val node = CapturedNode(
            id = "object-${nodesByHeapId.size + 1}",
            depth = depth,
            heapObject = heapObject,
        )
        nodesByHeapId[heapObjectId] = node
        pending.add(node)
        nodes.put(nodeJson(node))
        return node
    }

    private fun nodeJson(node: CapturedNode): JSONObject {
        val (kind, className, shallowSizeBytes) = when (val heapObject = node.heapObject) {
            is HeapObject.HeapClass -> ObjectDescription("class", heapObject.name, heapObject.recordSize.toLong())
            is HeapObject.HeapInstance -> ObjectDescription("instance", heapObject.instanceClassName, heapObject.byteSize.toLong())
            is HeapObject.HeapObjectArray -> ObjectDescription("object_array", heapObject.arrayClassName, heapObject.byteSize.toLong())
            is HeapObject.HeapPrimitiveArray -> ObjectDescription("primitive_array", heapObject.arrayClassName, heapObject.byteSize.toLong())
        }
        return JSONObject()
            .put("id", node.id)
            .put("kind", kind)
            .put("className", className)
            .put("shallowSizeBytes", shallowSizeBytes)
            .put("depth", node.depth)
    }

    private fun rootJson(root: GcRoot, index: Int, objectId: String?): JSONObject {
        val json = JSONObject()
            .put("id", "root-${index + 1}")
            .put("kind", rootKind(root))
            .put("objectId", objectId ?: JSONObject.NULL)
        when (root) {
            is GcRoot.JniLocal -> json
                .put("threadSerialNumber", root.threadSerialNumber)
                .put("frameNumber", root.frameNumber)
            is GcRoot.JniMonitor -> json
                .put("stackDepth", root.stackDepth)
            else -> Unit
        }
        return json
    }

    private fun rootKind(root: GcRoot): String = when (root) {
        is GcRoot.JniGlobal -> "jni_global"
        is GcRoot.JniLocal -> "jni_local"
        is GcRoot.JniMonitor -> "jni_monitor"
        else -> error("Unsupported JNI root ${root.javaClass.name}.")
    }

    private fun outgoingReferences(heapObject: HeapObject): Sequence<ObjectReference> = when (heapObject) {
        is HeapObject.HeapInstance -> heapObject.readFields().mapNotNull { field ->
            field.value.asNonNullObjectId?.let { targetId ->
                ObjectReference(
                    targetHeapObjectId = targetId,
                    kind = "instance_field",
                    label = field.name,
                    declaringClass = field.declaringClass.name,
                )
            }
        }
        is HeapObject.HeapClass -> heapObject.readStaticFields().mapNotNull { field ->
            field.value.asNonNullObjectId?.let { targetId ->
                ObjectReference(
                    targetHeapObjectId = targetId,
                    kind = "static_field",
                    label = field.name,
                    declaringClass = field.declaringClass.name,
                )
            }
        }
        is HeapObject.HeapObjectArray -> heapObject.readElements().withIndex().mapNotNull { indexed ->
            indexed.value.asNonNullObjectId?.let { targetId ->
                ObjectReference(
                    targetHeapObjectId = targetId,
                    kind = "array_element",
                    label = "[${indexed.index}]",
                    declaringClass = null,
                )
            }
        }
        is HeapObject.HeapPrimitiveArray -> emptySequence()
    }

    private data class CapturedNode(
        val id: String,
        val depth: Int,
        val heapObject: HeapObject,
    )

    private data class ObjectDescription(
        val kind: String,
        val className: String,
        val shallowSizeBytes: Long?,
    )

    private data class ObjectReference(
        val targetHeapObjectId: Long,
        val kind: String,
        val label: String,
        val declaringClass: String?,
    )
}
