package ai.ansight.tools.visualtree

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AndroidUiEvidence
import ai.ansight.runtime.BinaryTransferDescriptor
import ai.ansight.runtime.PairingFileTransferWireProtocol
import ai.ansight.runtime.ToolScope
import ai.ansight.runtime.androidUiTool
import ai.ansight.runtime.intArg
import ai.ansight.runtime.sendBinaryTransfer

object AndroidVisualTreeTools {
    fun create(): List<AndroidTool> = listOf(
        androidUiTool(
            VisualTreeToolIds.GetVisualTree,
            "Get Visual Tree",
            "Returns the current Android View hierarchy.",
        ) { _, _ ->
            AndroidToolResult.success(AndroidUiEvidence.visualTree())
        },
        androidUiTool(
            VisualTreeToolIds.GetScreenshot,
            "Get Screenshot",
            "Captures a screenshot of the current Android activity.",
        ) { args, context ->
            val screenshot = AndroidUiEvidence.captureScreenshot(
                format = args["format"] ?: "jpeg",
                quality = args.intArg("quality", context.options.sessionJpegCapture?.quality ?: 80),
                maxWidth = args["maxWidth"]?.toIntOrNull() ?: context.options.sessionJpegCapture?.maxWidth,
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

            context.transport?.takeIf { it.isOpen }?.let { transport ->
                Thread {
                    transport.sendBinaryTransfer(transferId, screenshot.bytes, chunkBytes)
                }.apply {
                    name = "AnsightAndroidScreenshotTransfer"
                    isDaemon = true
                    start()
                }
            }
            AndroidToolResult.success(descriptor)
        },
        androidUiTool(
            VisualTreeToolIds.InspectNode,
            "Inspect Node",
            "Returns one node from the current visual tree.",
        ) { args, _ ->
            val id = args["id"] ?: args["nodeId"] ?: return@androidUiTool AndroidToolResult.failure("Node id is required.", "node_id_required")
            AndroidToolResult.success(AndroidUiEvidence.inspectNode(id))
        },
        androidUiTool(
            VisualTreeToolIds.ShowOverlay,
            "Show Overlay",
            "Shows a rectangular diagnostic overlay.",
            ToolScope.Write,
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
            ToolScope.Write,
        ) { args, _ ->
            AndroidToolResult.success(AndroidUiEvidence.updateOverlay(args))
        },
        androidUiTool(
            VisualTreeToolIds.RemoveOverlay,
            "Remove Overlay",
            "Removes a diagnostic overlay.",
            ToolScope.Delete,
        ) { args, _ ->
            val id = args["id"] ?: return@androidUiTool AndroidToolResult.failure("Overlay id is required.", "overlay_id_required")
            AndroidToolResult.success(AndroidUiEvidence.removeOverlay(id))
        },
        androidUiTool(
            VisualTreeToolIds.ClearOverlays,
            "Clear Overlays",
            "Clears diagnostic overlays.",
            ToolScope.Delete,
        ) { _, _ ->
            AndroidToolResult.success(AndroidUiEvidence.clearOverlays())
        },
    )
}
