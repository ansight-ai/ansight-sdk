# Ansight Android JNI Reference Diagnostics

This package exposes the `ToolPolicy.Read`
`jni_references.capture_graph` app tool for Android.

The collector asks ART for an HPROF snapshot, uses Shark to index it, and walks
only objects reachable from JNI global, local, or monitor roots. The returned
JSON contains opaque per-capture ids, class names, shallow sizes, root
metadata, and reference edges. It deliberately omits raw heap ids, JNI handle
addresses, primitive values, strings, and other field values.

The default result is bounded to 512 nodes, 1024 edges, and depth 4. Tool
arguments can lower those limits or raise them to the configured maxima:

```json
{
  "maxNodes": 1024,
  "maxEdges": 2048,
  "maxDepth": 6
}
```

Register the suite directly when not using the aggregate `ansight-android`
package:

```kotlin
val options = AnsightOptions.createBuilder()
    .withJniReferenceDiagnosticsTools {
        maximumGraphNodes(4096)
        maximumGraphEdges(8192)
        maximumGraphDepth(12)
    }
    .build()
```

Heap dumping briefly pauses the app and indexing uses additional memory.
Capture is serialized, intended for explicit development diagnostics, and
always deletes the temporary HPROF file after graph construction.
