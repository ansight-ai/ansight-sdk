package ai.ansight.tools.visualtree

import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult

interface AndroidVisualTreeProvider {
    val source: String
    val displayName: String

    fun getVisualTree(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult
    fun inspectNode(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult
}

data class AndroidVisualTreeActionRequest(
    val nodeId: String,
    val action: String,
    val value: Any?,
    val options: org.json.JSONObject,
)

interface AndroidVisualTreeInteractionProvider {
    fun performAction(
        request: AndroidVisualTreeActionRequest,
        context: AndroidToolExecutionContext,
    ): AndroidToolResult
}
