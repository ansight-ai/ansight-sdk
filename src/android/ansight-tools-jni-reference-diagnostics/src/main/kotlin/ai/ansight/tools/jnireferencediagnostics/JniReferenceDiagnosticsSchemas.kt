package ai.ansight.tools.jnireferencediagnostics

import ai.ansight.runtime.ToolSchema

internal object JniReferenceDiagnosticsSchemas {
    private val root = ToolSchema.obj(
        description = "A JNI root and the opaque captured object it retains.",
        properties = mapOf(
            "id" to ToolSchema.string("Opaque root id scoped to this capture."),
            "kind" to ToolSchema.string(
                "JNI root kind.",
                enumValues = listOf("jni_global", "jni_local", "jni_monitor"),
            ),
            "objectId" to ToolSchema.string("Opaque target object id, or null when omitted by a capture limit.", nullable = true),
            "threadSerialNumber" to ToolSchema.integer("HPROF thread serial for a local JNI root.", nullable = true),
            "frameNumber" to ToolSchema.integer("HPROF frame number for a local JNI root.", nullable = true),
            "stackDepth" to ToolSchema.integer("Stack depth for a JNI monitor root.", nullable = true),
        ),
        required = listOf("id", "kind", "objectId"),
    )
    private val node = ToolSchema.obj(
        description = "A redacted heap object reachable from a JNI root.",
        properties = mapOf(
            "id" to ToolSchema.string("Opaque object id scoped to this capture."),
            "kind" to ToolSchema.string(
                "Heap object kind.",
                enumValues = listOf("class", "instance", "object_array", "primitive_array"),
            ),
            "className" to ToolSchema.string("Runtime class name."),
            "shallowSizeBytes" to ToolSchema.integer("Best available shallow object size.", nullable = true),
            "depth" to ToolSchema.integer("Shortest captured distance from a JNI root."),
        ),
        required = listOf("id", "kind", "className", "shallowSizeBytes", "depth"),
    )
    private val edge = ToolSchema.obj(
        description = "A redacted object-reference edge. Values are never included.",
        properties = mapOf(
            "from" to ToolSchema.string("Opaque source object id."),
            "to" to ToolSchema.string("Opaque target object id."),
            "kind" to ToolSchema.string(
                "Reference kind.",
                enumValues = listOf("instance_field", "static_field", "array_element"),
            ),
            "label" to ToolSchema.string("Field name or array index."),
            "declaringClass" to ToolSchema.string("Declaring class for a field edge.", nullable = true),
        ),
        required = listOf("from", "to", "kind", "label", "declaringClass"),
    )

    val captureGraphArguments = ToolSchema.obj(
        description = "Bounds for a JNI-rooted object-reference graph capture.",
        properties = mapOf(
            "maxNodes" to ToolSchema.integer("Maximum objects to return."),
            "maxEdges" to ToolSchema.integer("Maximum reference edges to return."),
            "maxDepth" to ToolSchema.integer("Maximum reference distance from a JNI root."),
        ),
    )

    val captureGraphResult = ToolSchema.obj(
        description = "Bounded, redacted object-reference graph rooted at JNI references.",
        properties = mapOf(
            "schemaVersion" to ToolSchema.string("Graph payload schema version."),
            "capturedAtUtc" to ToolSchema.string("UTC timestamp for the heap snapshot.", format = "date-time"),
            "provider" to ToolSchema.string("Capture implementation identifier."),
            "jniRootCount" to ToolSchema.integer("Total JNI roots in the heap snapshot."),
            "jniGlobalRootCount" to ToolSchema.integer("JNI global roots in the heap snapshot."),
            "jniLocalRootCount" to ToolSchema.integer("JNI local roots in the heap snapshot."),
            "jniMonitorRootCount" to ToolSchema.integer("JNI monitor roots in the heap snapshot."),
            "heapObjectCount" to ToolSchema.integer("Objects indexed from the heap snapshot."),
            "heapDumpBytes" to ToolSchema.integer("Temporary HPROF snapshot size."),
            "captureDurationMilliseconds" to ToolSchema.integer("Capture and graph-building duration."),
            "limits" to ToolSchema.obj(additionalProperties = true),
            "roots" to ToolSchema.array(root, "Returned JNI roots."),
            "nodes" to ToolSchema.array(node, "Returned heap objects."),
            "edges" to ToolSchema.array(edge, "Returned object references."),
            "truncated" to ToolSchema.bool("Whether any roots, nodes, edges, or depth were omitted."),
            "truncationReasons" to ToolSchema.array(ToolSchema.string(), "Applied capture bounds."),
        ),
        required = listOf(
            "schemaVersion", "capturedAtUtc", "provider", "jniRootCount",
            "jniGlobalRootCount", "jniLocalRootCount", "jniMonitorRootCount",
            "heapObjectCount", "heapDumpBytes", "captureDurationMilliseconds",
            "limits", "roots", "nodes", "edges", "truncated", "truncationReasons",
        ),
    )
}
