package ai.ansight.tools.filedescriptordiagnostics

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AnsightClock
import ai.ansight.runtime.FunctionAndroidTool
import ai.ansight.runtime.ToolDefinition
import ai.ansight.runtime.ToolPolicy
import ai.ansight.runtime.ToolSchema
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale

object AndroidFileDescriptorDiagnosticsTools {
    @JvmStatic
    @JvmOverloads
    fun create(
        options: AndroidFileDescriptorDiagnosticsOptions = AndroidFileDescriptorDiagnosticsOptions.Default,
    ): List<AndroidTool> = create(options, AndroidSystemFileDescriptorCollector)

    internal fun create(
        options: AndroidFileDescriptorDiagnosticsOptions,
        collector: FileDescriptorCollector,
    ): List<AndroidTool> = listOf(
        tool(
            id = FileDescriptorDiagnosticsToolIds.ListOpen,
            name = "List Open File Descriptors",
            description = "Lists live file descriptors owned by the current app process.",
            argumentsSchema = FileDescriptorDiagnosticsSchemas.listOpenArguments,
            resultSchema = FileDescriptorDiagnosticsSchemas.listOpenResult,
        ) { arguments ->
            runTool("file_descriptor_list_failed") {
                val snapshot = collector.snapshot(options)
                val maximum = parseMaximum(arguments, options)
                val kindFilter = parseKind(arguments["kind"])
                val targetFilter = arguments["targetContains"]?.trim()?.lowercase(Locale.US)?.ifBlank { null }
                val matching = snapshot.descriptors.filter { descriptor ->
                    (kindFilter == null || descriptor.kind == kindFilter) &&
                        (targetFilter == null || descriptor.target?.lowercase(Locale.US)?.contains(targetFilter) == true)
                }
                val returned = matching.take(maximum)
                AndroidToolResult.success(
                    snapshotJson(snapshot)
                        .put("count", snapshot.descriptors.size)
                        .put("matchedCount", matching.size)
                        .put("returnedCount", returned.size)
                        .put("descriptors", JSONArray(returned.map { it.toJson() }))
                        .put("truncated", returned.size < matching.size),
                )
            }
        },
        tool(
            id = FileDescriptorDiagnosticsToolIds.CountOpen,
            name = "Count Open File Descriptors",
            description = "Counts live file descriptors owned by the current app process without returning descriptor details.",
            argumentsSchema = FileDescriptorDiagnosticsSchemas.countOpenArguments,
            resultSchema = FileDescriptorDiagnosticsSchemas.countOpenResult,
        ) {
            runTool("file_descriptor_count_failed") {
                val snapshot = collector.count()
                AndroidToolResult.success(JSONObject().put("count", snapshot.count))
            }
        },
        tool(
            id = FileDescriptorDiagnosticsToolIds.Inspect,
            name = "Inspect File Descriptor",
            description = "Returns metadata for one live file descriptor in the current app process.",
            argumentsSchema = FileDescriptorDiagnosticsSchemas.inspectArguments,
            resultSchema = FileDescriptorDiagnosticsSchemas.inspectResult,
        ) { arguments ->
            val descriptor = arguments["descriptor"]?.toIntOrNull()
                ?: return@tool AndroidToolResult.failure("Argument 'descriptor' is required.", "file_descriptor_invalid_argument")
            if (descriptor < 0) {
                return@tool AndroidToolResult.failure("Argument 'descriptor' must be non-negative.", "file_descriptor_invalid_argument")
            }
            runTool("file_descriptor_inspect_failed") {
                val info = collector.inspect(descriptor, options.includeTargets)
                    ?: return@runTool AndroidToolResult.failure(
                        "File descriptor $descriptor is not open.",
                        "file_descriptor_not_open",
                    )
                AndroidToolResult.success(
                    JSONObject()
                        .put("descriptor", info.toJson())
                        .put("capturedAtUtc", AnsightClock.isoNow()),
                )
            }
        },
        tool(
            id = FileDescriptorDiagnosticsToolIds.GetUsage,
            name = "Get File Descriptor Usage",
            description = "Reports current open descriptor usage against the process soft and hard limits.",
            argumentsSchema = FileDescriptorDiagnosticsSchemas.getUsageArguments,
            resultSchema = FileDescriptorDiagnosticsSchemas.getUsageResult,
        ) {
            runTool("file_descriptor_usage_failed") {
                val snapshot = collector.count()
                val openCount = snapshot.count.toLong()
                val softLimit = snapshot.limits.softLimit
                val available = softLimit
                    ?.takeIf { snapshot.scanComplete }
                    ?.let { (it - openCount).coerceAtLeast(0) }
                val utilization = softLimit
                    ?.takeIf { snapshot.scanComplete && it > 0 }
                    ?.let { openCount.toDouble() / it.toDouble() * 100.0 }
                AndroidToolResult.success(
                    snapshotJson(snapshot.scanComplete, snapshot.scannedDescriptorLimit)
                        .put("openCount", openCount)
                        .put("softLimit", softLimit ?: JSONObject.NULL)
                        .put("hardLimit", snapshot.limits.hardLimit ?: JSONObject.NULL)
                        .put("hardLimitUnlimited", snapshot.limits.hardLimitUnlimited)
                        .put("availableBeforeSoftLimit", available ?: JSONObject.NULL)
                        .put("utilizationPercent", utilization ?: JSONObject.NULL),
                )
            }
        },
    )

    private fun tool(
        id: String,
        name: String,
        description: String,
        argumentsSchema: ToolSchema,
        resultSchema: ToolSchema,
        handler: (Map<String, String>) -> AndroidToolResult,
    ): AndroidTool = FunctionAndroidTool(
        ToolDefinition(
            id = id,
            name = name,
            description = description,
            category = "file_descriptors",
            policy = ToolPolicy.Read,
            keywords = "file descriptors handles open files sockets pipes limits diagnostics",
            argumentsSchema = argumentsSchema,
            resultSchema = resultSchema,
        ),
    ) { arguments, _ -> handler(arguments) }

    private fun snapshotJson(snapshot: FileDescriptorSnapshot): JSONObject =
        snapshotJson(snapshot.scanComplete, snapshot.scannedDescriptorLimit)

    private fun snapshotJson(scanComplete: Boolean, scannedDescriptorLimit: Int): JSONObject = JSONObject()
        .put("scanComplete", scanComplete)
        .put("scannedDescriptorLimit", scannedDescriptorLimit)
        .put("capturedAtUtc", AnsightClock.isoNow())

    private fun parseMaximum(
        arguments: Map<String, String>,
        options: AndroidFileDescriptorDiagnosticsOptions,
    ): Int {
        val maximumAllowed = options.validatedMaximumReturnedDescriptors
        val rawValue = arguments["maxEntries"] ?: return minOf(256, maximumAllowed)
        val value = rawValue.toIntOrNull()
            ?: throw IllegalArgumentException("Argument 'maxEntries' must be an integer.")
        require(value in 1..maximumAllowed) {
            "Argument 'maxEntries' must be between 1 and $maximumAllowed."
        }
        return value
    }

    private fun parseKind(rawValue: String?): FileDescriptorKind? {
        val value = rawValue?.trim()?.lowercase(Locale.US)?.ifBlank { null } ?: return null
        return FileDescriptorKind.fromWireName(value)
            ?: throw IllegalArgumentException("Unknown file descriptor kind '$value'.")
    }

    private inline fun runTool(errorCode: String, block: () -> AndroidToolResult): AndroidToolResult =
        try {
            block()
        } catch (error: Exception) {
            AndroidToolResult.failure(error.message ?: "File descriptor diagnostics failed.", errorCode)
        }
}
