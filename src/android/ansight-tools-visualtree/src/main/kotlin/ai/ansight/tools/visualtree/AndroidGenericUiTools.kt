package ai.ansight.tools.visualtree

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.FunctionJsonAndroidTool
import ai.ansight.runtime.ToolDefinition
import ai.ansight.runtime.ToolSchema
import ai.ansight.runtime.ToolPolicy
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale

internal object AndroidGenericUiTools {
    fun create(): List<AndroidTool> = listOf(queryTool(), actionTool(), waitTool())

    private fun queryTool(): AndroidTool = FunctionJsonAndroidTool(
        ToolDefinition(
            id = VisualTreeToolIds.QueryNodes,
            name = "Query UI Nodes",
            description = "Captures or reuses a UI snapshot and returns framework-neutral node references.",
            category = "ui",
            policy = ToolPolicy.Read,
            keywords = "ui query find node selector automation id role text type",
            argumentsSchema = queryArgumentsSchema,
            resultSchema = queryResultSchema,
        ),
        handler = ::query,
    )

    private fun actionTool(): AndroidTool = FunctionJsonAndroidTool(
        ToolDefinition(
            id = VisualTreeToolIds.PerformAction,
            name = "Perform UI Action",
            description = "Performs a generic action against a current snapshot-scoped UI node.",
            category = "ui",
            policy = ToolPolicy.Write,
            keywords = "ui action tap focus set value toggle select node snapshot",
            argumentsSchema = ToolSchema.obj(
                properties = mapOf(
                    "reference" to referenceSchema,
                    "source" to ToolSchema.string(nullable = true),
                    "snapshotId" to ToolSchema.string(nullable = true),
                    "nodeId" to ToolSchema.string(nullable = true),
                    "action" to ToolSchema.string(enumValues = listOf("tap", "focus", "unfocus", "setValue", "typeText", "toggle", "select", "selectTab")),
                    "value" to ToolSchema.string(nullable = true),
                    "index" to ToolSchema.integer(nullable = true),
                    "checked" to ToolSchema.bool(nullable = true),
                    "options" to ToolSchema.obj(additionalProperties = true, nullable = true),
                ),
                required = listOf("action"),
            ),
            resultSchema = ToolSchema.obj(
                properties = mapOf(
                    "source" to ToolSchema.string(),
                    "action" to ToolSchema.string(),
                    "reference" to referenceSchema,
                ),
                required = listOf("source", "action", "reference"),
                additionalProperties = true,
            ),
            prerequisiteToolIds = listOf(VisualTreeToolIds.QueryNodes),
        ),
    ) { arguments, context -> performAction(arguments, context) }

    private fun waitTool(): AndroidTool = FunctionJsonAndroidTool(
        ToolDefinition(
            id = VisualTreeToolIds.Wait,
            name = "Wait For UI",
            description = "Polls generic UI snapshots until a node condition is met.",
            category = "ui",
            policy = ToolPolicy.Read,
            keywords = "ui wait poll condition exists visible enabled gone",
            argumentsSchema = ToolSchema.obj(
                properties = queryProperties + mapOf(
                    "condition" to ToolSchema.string(enumValues = listOf("exists", "notExists", "visible", "enabled")),
                    "timeoutMilliseconds" to ToolSchema.integer(),
                    "pollMilliseconds" to ToolSchema.integer(),
                ),
                required = listOf("condition"),
            ),
            resultSchema = ToolSchema.obj(
                properties = mapOf(
                    "condition" to ToolSchema.string(),
                    "matched" to ToolSchema.bool(),
                    "elapsedMilliseconds" to ToolSchema.integer(),
                    "query" to queryResultSchema,
                ),
                required = listOf("condition", "matched", "elapsedMilliseconds", "query"),
            ),
        ),
    ) { arguments, context -> wait(arguments, context) }

    private fun query(arguments: JSONObject, context: AndroidToolExecutionContext): AndroidToolResult {
        val source = arguments.optionalString("source")
        val requestedSnapshotId = arguments.optionalString("snapshotId")
        val snapshot = if (requestedSnapshotId == null) {
            val capture = AndroidVisualTreeSnapshotStore.capture(source, arguments.toStringMap(), context)
            if (!capture.success) return capture
            val snapshotId = capture.payload?.optString("snapshotId").orEmpty()
            val (captured, error) = AndroidVisualTreeSnapshotStore.current(snapshotId, source)
            if (captured == null) return error!!
            captured
        } else {
            val (stored, error) = AndroidVisualTreeSnapshotStore.current(requestedSnapshotId, source)
            if (stored == null) return error!!
            stored
        }

        val maxResults = arguments.optInt("maxResults", 50).coerceIn(1, 500)
        val matches = JSONArray()
        var totalMatches = 0
        snapshot.payload.optJSONObject("root")?.let { root ->
            enumerate(root).forEach { node ->
                if (!matches(snapshot.payload, node, arguments)) return@forEach
                totalMatches++
                if (matches.length() < maxResults) {
                    val match = JSONObject(node.toString())
                    val nodeId = node.optString("id")
                    val type = resolveType(snapshot.payload, node)
                    match
                        .put("reference", AndroidVisualTreeSnapshotStore.reference(snapshot, nodeId))
                        .put("type", type ?: JSONObject.NULL)
                        .put("visible", readState(snapshot.payload, node, "visible", 1))
                        .put("enabled", readState(snapshot.payload, node, "enabled", 2))
                    if (!match.has("supportedActions")) match.put("supportedActions", inferActions(type))
                    matches.put(match)
                }
            }
        }
        return AndroidToolResult.success(JSONObject()
            .put("source", snapshot.source)
            .put("snapshotId", snapshot.snapshotId)
            .put("revision", snapshot.revision)
            .put("count", matches.length())
            .put("totalMatches", totalMatches)
            .put("truncated", totalMatches > matches.length())
            .put("matches", matches))
    }

    private fun performAction(arguments: JSONObject, context: AndroidToolExecutionContext): AndroidToolResult {
        val reference = arguments.optJSONObject("reference")
        val snapshotId = arguments.optionalString("snapshotId")
            ?: reference?.optionalString("snapshotId")
            ?: return AndroidToolResult.failure(
                "A reference, or both snapshotId and nodeId, is required.",
                "ui_action_reference_required",
            )
        val nodeId = arguments.optionalString("nodeId")
            ?: reference?.optionalString("nodeId")
            ?: return AndroidToolResult.failure(
                "A reference, or both snapshotId and nodeId, is required.",
                "ui_action_reference_required",
            )
        val source = arguments.optionalString("source") ?: reference?.optionalString("source")
        val (snapshot, referenceError) = AndroidVisualTreeSnapshotStore.validateNode(snapshotId, source, nodeId)
        if (snapshot == null) return referenceError!!
        val provider = AndroidVisualTreeProviderRegistry.provider(snapshot.source)
        if (provider !is AndroidVisualTreeInteractionProvider) {
            return AndroidToolResult.failure(
                "Visual-tree source '${snapshot.source}' does not support generic UI actions.",
                "ui_action_not_supported",
            )
        }
        val value = when {
            arguments.has("value") -> arguments.opt("value")
            arguments.has("index") -> arguments.opt("index")
            arguments.has("checked") -> arguments.opt("checked")
            else -> null
        }
        val action = arguments.getString("action")
        val result = provider.performAction(
            AndroidVisualTreeActionRequest(
                nodeId,
                action,
                value,
                arguments.optJSONObject("options") ?: JSONObject(),
            ),
            context,
        )
        if (!result.success) {
            if (result.errorCode in setOf("visual_tree_node_not_found", "node_not_found", "maui_node_not_found", "dom_node_not_found")) {
                return AndroidToolResult.failure(
                    "Node '$nodeId' is no longer valid for snapshot '$snapshotId'. Refresh the query and retry.",
                    "stale_node_reference",
                    JSONObject()
                        .put("reference", AndroidVisualTreeSnapshotStore.reference(snapshot, nodeId))
                        .put("providerError", result.errorCode)
                        .put("refreshWith", VisualTreeToolIds.QueryNodes),
                )
            }
            return result
        }
        val payload = result.payload ?: JSONObject()
        payload
            .put("source", snapshot.source)
            .put("action", action)
            .put("reference", AndroidVisualTreeSnapshotStore.reference(snapshot, nodeId))
        return AndroidToolResult.success(payload, result.message)
    }

    private fun wait(arguments: JSONObject, context: AndroidToolExecutionContext): AndroidToolResult {
        val condition = arguments.getString("condition")
        val timeout = arguments.optInt("timeoutMilliseconds", 5_000).coerceIn(1, 60_000)
        val poll = arguments.optInt("pollMilliseconds", 100).coerceIn(10, 5_000)
        val started = android.os.SystemClock.elapsedRealtime()
        var lastQuery: JSONObject? = null
        while (android.os.SystemClock.elapsedRealtime() - started <= timeout) {
            val queryArguments = JSONObject(arguments.toString())
            queryArguments.remove("condition")
            queryArguments.remove("timeoutMilliseconds")
            queryArguments.remove("pollMilliseconds")
            queryArguments.remove("snapshotId")
            if (condition == "visible") queryArguments.put("visible", true)
            if (condition == "enabled") queryArguments.put("enabled", true)
            val queryResult = query(queryArguments, context)
            if (!queryResult.success) return queryResult
            lastQuery = queryResult.payload
            val count = lastQuery?.optInt("count") ?: 0
            val matched = if (condition == "notExists") count == 0 else count > 0
            if (matched) {
                return AndroidToolResult.success(JSONObject()
                    .put("condition", condition)
                    .put("matched", true)
                    .put("elapsedMilliseconds", android.os.SystemClock.elapsedRealtime() - started)
                    .put("query", lastQuery))
            }
            Thread.sleep(poll.toLong())
        }
        return AndroidToolResult.failure(
            "Timed out after ${timeout}ms waiting for UI condition '$condition'.",
            "ui_wait_timeout",
            JSONObject()
                .put("condition", condition)
                .put("matched", false)
                .put("elapsedMilliseconds", android.os.SystemClock.elapsedRealtime() - started)
                .put("lastQuery", lastQuery ?: JSONObject.NULL),
        )
    }

    private fun enumerate(root: JSONObject): Sequence<JSONObject> = sequence {
        yield(root)
        val children = root.optJSONArray("children") ?: return@sequence
        for (index in 0 until children.length()) {
            val child = children.optJSONObject(index) ?: continue
            yieldAll(enumerate(child))
        }
    }

    private fun matches(payload: JSONObject, node: JSONObject, arguments: JSONObject): Boolean {
        val nodeId = node.optString("id")
        if (nodeId.isBlank()) return false
        if (!equalsFilter(nodeId, arguments, "nodeId")
            || !equalsFilter(node.optionalString("automationId"), arguments, "automationId")
            || !equalsFilter(node.optionalString("role"), arguments, "role")
            || !containsFilter(resolveType(payload, node), arguments, "type")
            || !containsFilter(searchText(node), arguments, "textContains")) return false
        if (arguments.has("visible") && readState(payload, node, "visible", 1) != arguments.getBoolean("visible")) return false
        if (arguments.has("enabled") && readState(payload, node, "enabled", 2) != arguments.getBoolean("enabled")) return false
        val requiredAction = arguments.optionalString("action")
        if (requiredAction != null) {
            val actions = node.optJSONArray("supportedActions") ?: inferActions(resolveType(payload, node))
            if ((0 until actions.length()).none { actions.optString(it).equals(requiredAction, ignoreCase = true) }) return false
        }
        return true
    }

    private fun equalsFilter(value: String?, arguments: JSONObject, name: String): Boolean =
        arguments.optionalString(name)?.let { it.equals(value, ignoreCase = true) } ?: true

    private fun containsFilter(value: String?, arguments: JSONObject, name: String): Boolean =
        arguments.optionalString(name)?.let { value?.contains(it, ignoreCase = true) == true } ?: true

    private fun resolveType(payload: JSONObject, node: JSONObject): String? {
        node.optionalString("type")?.let { return it }
        val typeId = node.optInt("typeId", -1)
        val types = payload.optJSONArray("types") ?: return null
        return if (typeId in 0 until types.length()) types.optString(typeId) else null
    }

    private fun searchText(node: JSONObject): String = listOfNotNull(
        node.optionalString("label"),
        node.optionalString("title"),
        node.optJSONObject("visual")?.optionalString("text"),
        node.optJSONObject("visual")?.optionalString("value"),
    ).joinToString(" ")

    private fun readState(payload: JSONObject, node: JSONObject, name: String, fallbackBit: Int): Boolean {
        if (node.has(name)) return node.optBoolean(name)
        val bit = payload.optJSONObject("flagBits")?.optInt(name, fallbackBit) ?: fallbackBit
        return node.has("flags") && node.optInt("flags") and bit == bit
    }

    private fun inferActions(type: String?): JSONArray {
        val normalized = type?.lowercase(Locale.US).orEmpty()
        val actions = JSONArray()
        if ("button" in normalized || "tap" in normalized) actions.put("tap")
        if ("entry" in normalized || "editor" in normalized || "textfield" in normalized) {
            actions.put("focus")
            actions.put("setValue")
        }
        if ("checkbox" in normalized || "switch" in normalized) actions.put("toggle")
        if ("picker" in normalized) actions.put("select")
        return actions
    }

    private fun JSONObject.toStringMap(): Map<String, String> = keys().asSequence()
        .filter { !isNull(it) }
        .associateWith { opt(it)?.toString().orEmpty() }

    private fun JSONObject.optionalString(name: String): String? =
        optString(name).trim().takeIf { has(name) && !isNull(name) && it.isNotEmpty() }

    private val referenceSchema = ToolSchema.obj(
        properties = mapOf(
            "source" to ToolSchema.string(),
            "snapshotId" to ToolSchema.string(),
            "revision" to ToolSchema.integer(),
            "nodeId" to ToolSchema.string(),
        ),
        required = listOf("source", "snapshotId", "revision", "nodeId"),
    )

    private val queryProperties = mapOf(
        "source" to ToolSchema.string(nullable = true),
        "snapshotId" to ToolSchema.string(nullable = true),
        "nodeId" to ToolSchema.string(nullable = true),
        "automationId" to ToolSchema.string(nullable = true),
        "role" to ToolSchema.string(nullable = true),
        "type" to ToolSchema.string(nullable = true),
        "textContains" to ToolSchema.string(nullable = true),
        "action" to ToolSchema.string(nullable = true),
        "visible" to ToolSchema.bool(nullable = true),
        "enabled" to ToolSchema.bool(nullable = true),
        "maxResults" to ToolSchema.integer(),
        "includeBounds" to ToolSchema.bool(),
        "includeComputedStyles" to ToolSchema.bool(),
        "includeProperties" to ToolSchema.bool(),
        "includeInactivePages" to ToolSchema.bool(),
        "root" to ToolSchema.string(nullable = true),
        "rootNodeId" to ToolSchema.string(nullable = true),
        "maxDepth" to ToolSchema.integer(),
        "maxNodes" to ToolSchema.integer(),
    )

    private val queryArgumentsSchema = ToolSchema.obj(properties = queryProperties)
    private val queryResultSchema = ToolSchema.obj(
        properties = mapOf(
            "source" to ToolSchema.string(),
            "snapshotId" to ToolSchema.string(),
            "revision" to ToolSchema.integer(),
            "count" to ToolSchema.integer(),
            "totalMatches" to ToolSchema.integer(),
            "truncated" to ToolSchema.bool(),
            "matches" to ToolSchema.array(ToolSchema.obj(additionalProperties = true)),
        ),
        required = listOf("source", "snapshotId", "revision", "count", "totalMatches", "truncated", "matches"),
    )
}
