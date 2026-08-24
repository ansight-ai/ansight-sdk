package ai.ansight.tools.visualtree

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AndroidUiEvidence
import ai.ansight.runtime.AnsightSessionJpegCaptureOptions
import ai.ansight.runtime.BinaryTransferDescriptor
import ai.ansight.runtime.PairingFileTransferWireProtocol
import ai.ansight.runtime.ToolPolicy
import ai.ansight.runtime.androidUiTool
import ai.ansight.runtime.intArg
import ai.ansight.runtime.queueBinaryTransfer

object AndroidVisualTreeTools {
    fun create(): List<AndroidTool> = listOf(
        androidUiTool(
            VisualTreeToolIds.GetVisualTree,
            "Get Visual Tree",
            "Returns the current visual hierarchy for the requested source.",
        ) { args, context ->
            AndroidVisualTreeSnapshotStore.capture(args["source"], args, context)
        },
        androidUiTool(
            VisualTreeToolIds.GetScreenshot,
            "Get Screenshot",
            "Captures a screenshot of the current Android activity.",
        ) { args, context ->
            val screenshot = AndroidUiEvidence.captureScreenshot(
                format = args["format"] ?: "jpeg",
                quality = args.intArg(
                    "quality",
                    context.options.sessionJpegCapture?.quality ?: AnsightSessionJpegCaptureOptions.DefaultQuality,
                ),
                maxWidth = args["maxWidth"]?.toIntOrNull()
                    ?: context.options.sessionJpegCapture?.maxWidth
                    ?: AnsightSessionJpegCaptureOptions.DefaultMaxWidth,
            )
            val transferId = PairingFileTransferWireProtocol.newTransferId()
            val downloadId = args["downloadId"]?.trim()?.ifBlank { null } ?: context.requestId ?: transferId
            val chunkBytes = args.intArg("chunkBytes", 64 * 1024)
            val descriptor = BinaryTransferDescriptor(
                transferId = transferId,
                downloadId = downloadId,
                fileName = screenshot.fileName,
                mimeType = screenshot.mimeType,
                sizeBytes = screenshot.bytes.size.toLong(),
                chunkBytes = chunkBytes,
                status = if (context.transport?.isOpen == true) "started" else "unavailable",
            ).toJson()
                .put("width", screenshot.width)
                .put("height", screenshot.height)

            context.transport?.takeIf { it.isOpen }?.let {
                context.queueBinaryTransfer(transferId, screenshot.bytes, chunkBytes)
            }
            AndroidToolResult.success(descriptor)
        },
        androidUiTool(
            VisualTreeToolIds.InspectNode,
            "Inspect Node",
            "Returns one node from the current visual tree.",
        ) { args, context ->
            val reference = args["reference"]?.let { raw ->
                runCatching { org.json.JSONObject(raw) }.getOrNull()
            }
            val source = args["source"] ?: reference?.optString("source")?.takeIf { it.isNotBlank() }
            val nodeId = args["nodeId"] ?: args["id"]
                ?: reference?.optString("nodeId")?.takeIf { it.isNotBlank() }
                ?: return@androidUiTool AndroidToolResult.failure("Node id is required.", "node_id_required")
            val snapshotIdArgument = args["snapshotId"]
                ?: reference?.optString("snapshotId")?.takeIf { it.isNotBlank() }
            val providerArguments = args.toMutableMap().apply {
                put("nodeId", nodeId)
                source?.let { put("source", it) }
            }
            val snapshot = snapshotIdArgument?.let { snapshotId ->
                val (stored, error) = AndroidVisualTreeSnapshotStore.validateNode(snapshotId, source, nodeId)
                if (stored == null) return@androidUiTool error!!
                stored
            } ?: run {
                val capture = AndroidVisualTreeSnapshotStore.capture(source, providerArguments, context)
                if (!capture.success) return@androidUiTool capture
                val snapshotId = capture.payload?.optString("snapshotId").orEmpty()
                val (captured, error) = AndroidVisualTreeSnapshotStore.validateNode(snapshotId, source, nodeId)
                if (captured == null) return@androidUiTool error!!
                captured
            }
            val provider = AndroidVisualTreeProviderRegistry.provider(snapshot.source)
                ?: return@androidUiTool AndroidToolResult.failure(
                    "No visual tree provider is registered for source '${snapshot.source}'.",
                    "visual_tree_provider_not_found",
                )
            providerArguments["source"] = snapshot.source
            providerArguments["snapshotId"] = snapshot.snapshotId
            val result = provider.inspectNode(providerArguments, context)
            if (!result.success) {
                if (result.errorCode in setOf("visual_tree_node_not_found", "node_not_found", "dom_node_not_found")) {
                    return@androidUiTool AndroidToolResult.failure(
                        "Node '$nodeId' is no longer valid for snapshot '${snapshot.snapshotId}'.",
                        "stale_node_reference",
                        org.json.JSONObject()
                            .put("reference", AndroidVisualTreeSnapshotStore.reference(snapshot, nodeId))
                            .put("providerError", result.errorCode)
                            .put("refreshWith", VisualTreeToolIds.QueryNodes),
                    )
                }
                return@androidUiTool result
            }
            result.payload
                ?.put("source", snapshot.source)
                ?.put("snapshotId", snapshot.snapshotId)
                ?.put("revision", snapshot.revision)
                ?.put("reference", AndroidVisualTreeSnapshotStore.reference(snapshot, nodeId))
            result
        },
        androidUiTool(
            VisualTreeToolIds.ShowOverlay,
            "Show Overlay",
            "Shows a rectangular diagnostic overlay.",
            ToolPolicy.Write,
        ) { args, _ ->
            AndroidToolResult.success(AndroidUiEvidence.showOverlay(args))
        },
        androidUiTool(
            VisualTreeToolIds.GetOverlay,
            "Get Overlay",
            "Returns one diagnostic overlay.",
        ) { args, _ ->
            val id = args["id"] ?: return@androidUiTool AndroidToolResult.failure("Overlay id is required.", "overlay_id_required")
            AndroidToolResult.success(AndroidUiEvidence.getOverlay(id))
        },
        androidUiTool(
            VisualTreeToolIds.QueryOverlays,
            "Query Overlays",
            "Lists active diagnostic overlays.",
        ) { _, _ ->
            AndroidToolResult.success(AndroidUiEvidence.queryOverlays())
        },
        androidUiTool(
            VisualTreeToolIds.UpdateOverlay,
            "Update Overlay",
            "Updates a diagnostic overlay.",
            ToolPolicy.Write,
        ) { args, _ ->
            AndroidToolResult.success(AndroidUiEvidence.updateOverlay(args))
        },
        androidUiTool(
            VisualTreeToolIds.RemoveOverlay,
            "Remove Overlay",
            "Removes a diagnostic overlay.",
            ToolPolicy.Write,
        ) { args, _ ->
            val id = args["id"] ?: return@androidUiTool AndroidToolResult.failure("Overlay id is required.", "overlay_id_required")
            AndroidToolResult.success(AndroidUiEvidence.removeOverlay(id))
        },
        androidUiTool(
            VisualTreeToolIds.ClearOverlays,
            "Clear Overlays",
            "Clears diagnostic overlays.",
            ToolPolicy.Write,
        ) { _, _ ->
            AndroidToolResult.success(AndroidUiEvidence.clearOverlays())
        },
    ) + AndroidGenericUiTools.create()
}
