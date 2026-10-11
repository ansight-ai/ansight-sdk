package ai.ansight.runtime

internal class OfflineCaptureTransferTool : AndroidTool {
    override val definition = ToolDefinition("offline_capture.transfer", "Transfer offline captures",
        "Import retained offline sessions into this host, then purge acknowledged local copies.",
        "Diagnostics", ToolPolicy.Write, "offline captures import")
    override fun execute(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult {
        val result = AnsightRuntime.transferOfflineCaptures()
        return if (result.success) AndroidToolResult.success(message = result.message) else AndroidToolResult.failure(result.message)
    }
}
