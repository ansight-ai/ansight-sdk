package ai.ansight.tools.visualtree

import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AndroidUiEvidence

object AndroidNativeVisualTreeProvider : AndroidVisualTreeProvider {
    override val source: String = AndroidVisualTreeProviderRegistry.NativeSource
    override val displayName: String = "Native"

    override fun getVisualTree(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult {
        return AndroidToolResult.success(AndroidUiEvidence.visualTree())
    }

    override fun inspectNode(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult {
        val id = arguments["id"] ?: arguments["nodeId"] ?: return AndroidToolResult.failure("Node id is required.", "node_id_required")
        return AndroidToolResult.success(AndroidUiEvidence.inspectNode(id))
    }
}
