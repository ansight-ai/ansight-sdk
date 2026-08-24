package ai.ansight.tools.visualtree

import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AndroidUiEvidence

object AndroidNativeVisualTreeProvider : AndroidVisualTreeProvider, AndroidVisualTreeInteractionProvider {
    override val source: String = AndroidVisualTreeProviderRegistry.NativeSource
    override val displayName: String = "Native"

    override fun getVisualTree(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult {
        return AndroidToolResult.success(AndroidUiEvidence.visualTree())
    }

    override fun inspectNode(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult {
        val id = arguments["id"] ?: arguments["nodeId"] ?: return AndroidToolResult.failure("Node id is required.", "node_id_required")
        return AndroidToolResult.success(AndroidUiEvidence.inspectNode(id))
    }

    override fun performAction(
        request: AndroidVisualTreeActionRequest,
        context: AndroidToolExecutionContext,
    ): AndroidToolResult {
        return try {
            AndroidToolResult.success(AndroidUiEvidence.performAction(request.nodeId, request.action, request.value))
        } catch (error: IllegalArgumentException) {
            AndroidToolResult.failure(error.message ?: "The node was not found.", "visual_tree_node_not_found")
        } catch (error: UnsupportedOperationException) {
            AndroidToolResult.failure(error.message ?: "The action is unsupported.", "ui_action_not_supported")
        } catch (error: Exception) {
            AndroidToolResult.failure(error.message ?: "Native UI action failed.", "ui_action_failed")
        }
    }
}
