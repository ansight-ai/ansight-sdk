package ai.ansight.tools.jnireferencediagnostics

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.FunctionAndroidTool
import ai.ansight.runtime.ToolDefinition
import ai.ansight.runtime.ToolPolicy

object AndroidJniReferenceDiagnosticsTools {
    @JvmStatic
    @JvmOverloads
    fun create(
        options: AndroidJniReferenceDiagnosticsOptions = AndroidJniReferenceDiagnosticsOptions.Default,
    ): List<AndroidTool> = create(options, AndroidHprofJniReferenceGraphCollector)

    internal fun create(
        options: AndroidJniReferenceDiagnosticsOptions,
        collector: JniReferenceGraphCollector,
    ): List<AndroidTool> = listOf(
        FunctionAndroidTool(
            ToolDefinition(
                id = JniReferenceDiagnosticsToolIds.CaptureGraph,
                name = "Capture JNI Object-Reference Graph",
                description = "Captures a bounded, redacted object graph rooted at JNI references.",
                category = "jni_references",
                policy = ToolPolicy.Read,
                keywords = "jni java native references globals locals monitors heap graph diagnostics",
                argumentsSchema = JniReferenceDiagnosticsSchemas.captureGraphArguments,
                resultSchema = JniReferenceDiagnosticsSchemas.captureGraphResult,
            ),
        ) { arguments, context ->
            try {
                val limits = parseLimits(arguments, options)
                AndroidToolResult.success(collector.capture(context.application, limits))
            } catch (error: IllegalArgumentException) {
                AndroidToolResult.failure(
                    error.message ?: "JNI reference graph arguments are invalid.",
                    "jni_reference_graph_invalid_argument",
                )
            } catch (error: Exception) {
                AndroidToolResult.failure(
                    error.message ?: "JNI reference graph capture failed.",
                    "jni_reference_graph_capture_failed",
                )
            }
        },
    )

    private fun parseLimits(
        arguments: Map<String, String>,
        options: AndroidJniReferenceDiagnosticsOptions,
    ): JniReferenceGraphLimits = JniReferenceGraphLimits(
        maximumNodes = parseBound(
            arguments = arguments,
            name = "maxNodes",
            defaultValue = minOf(512, options.validatedMaximumGraphNodes),
            minimum = 1,
            maximum = options.validatedMaximumGraphNodes,
        ),
        maximumEdges = parseBound(
            arguments = arguments,
            name = "maxEdges",
            defaultValue = minOf(1_024, options.validatedMaximumGraphEdges),
            minimum = 1,
            maximum = options.validatedMaximumGraphEdges,
        ),
        maximumDepth = parseBound(
            arguments = arguments,
            name = "maxDepth",
            defaultValue = minOf(4, options.validatedMaximumGraphDepth),
            minimum = 0,
            maximum = options.validatedMaximumGraphDepth,
        ),
    )

    private fun parseBound(
        arguments: Map<String, String>,
        name: String,
        defaultValue: Int,
        minimum: Int,
        maximum: Int,
    ): Int {
        val rawValue = arguments[name] ?: return defaultValue
        val value = rawValue.toIntOrNull()
            ?: throw IllegalArgumentException("Argument '$name' must be an integer.")
        require(value in minimum..maximum) {
            "Argument '$name' must be between $minimum and $maximum."
        }
        return value
    }
}

object AndroidJniReferenceDiagnostics {
    @JvmStatic
    fun captureGraph(
        application: android.app.Application,
        maximumNodes: Int,
        maximumEdges: Int,
        maximumDepth: Int,
    ): String = AndroidHprofJniReferenceGraphCollector.capture(
        application,
        JniReferenceGraphLimits(
            maximumNodes = maximumNodes.coerceIn(1, 8_192),
            maximumEdges = maximumEdges.coerceIn(1, 16_384),
            maximumDepth = maximumDepth.coerceIn(0, 16),
        ),
    ).toString()
}
