package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale

enum class ToolScope {
    Read,
    Write,
    Delete,
}

enum class ToolSecurityLevel {
    Low,
    Medium,
    High,
    Critical,
}

data class ToolSecurity(
    val level: ToolSecurityLevel = ToolSecurityLevel.Low,
    val implications: List<String> = emptyList(),
) {
    fun toJson(): JSONObject = JSONObject()
        .put("level", level.name)
        .put("implications", JSONArray(implications))

    companion object {
        val Unspecified = ToolSecurity()
    }
}

data class ToolSchema(
    val type: String,
    val description: String? = null,
    val properties: Map<String, ToolSchema> = emptyMap(),
    val required: List<String> = emptyList(),
    val items: ToolSchema? = null,
    val enumValues: List<String> = emptyList(),
    val additionalProperties: Boolean = false,
    val nullable: Boolean = false,
    val format: String? = null,
) {
    fun toJson(): JSONObject {
        val json = JSONObject()
            .put("type", if (nullable) JSONArray(listOf(type, "null")) else type)
            .put("additionalProperties", additionalProperties)
        json.putIfNotNull("description", description)
        json.putIfNotNull("format", format)
        if (properties.isNotEmpty()) {
            val propertyJson = JSONObject()
            properties.entries.sortedBy { it.key }.forEach { propertyJson.put(it.key, it.value.toJson()) }
            json.put("properties", propertyJson)
        }
        if (required.isNotEmpty()) {
            json.put("required", JSONArray(required))
        }
        if (items != null) {
            json.put("items", items.toJson())
        }
        if (enumValues.isNotEmpty()) {
            json.put("enum", JSONArray(enumValues))
        }
        return json
    }

    companion object {
        fun obj(
            description: String? = null,
            properties: Map<String, ToolSchema> = emptyMap(),
            required: List<String> = emptyList(),
            additionalProperties: Boolean = false,
            nullable: Boolean = false,
        ) = ToolSchema("object", description, properties, required, additionalProperties = additionalProperties, nullable = nullable)

        fun array(items: ToolSchema, description: String? = null, nullable: Boolean = false) =
            ToolSchema("array", description, items = items, nullable = nullable)

        fun string(description: String? = null, enumValues: List<String> = emptyList(), nullable: Boolean = false, format: String? = null) =
            ToolSchema("string", description, enumValues = enumValues, nullable = nullable, format = format)

        fun integer(description: String? = null, nullable: Boolean = false) = ToolSchema("integer", description, nullable = nullable)
        fun number(description: String? = null, nullable: Boolean = false) = ToolSchema("number", description, nullable = nullable)
        fun bool(description: String? = null, nullable: Boolean = false) = ToolSchema("boolean", description, nullable = nullable)
    }
}

data class ToolDefinition(
    val id: String,
    val name: String,
    val description: String,
    val category: String,
    val scope: ToolScope,
    val keywords: String,
    val argumentsSchema: ToolSchema = ToolSchema.obj(additionalProperties = true),
    val resultSchema: ToolSchema = ToolSchema.obj(additionalProperties = true),
    val security: ToolSecurity = ToolSecurity.Unspecified,
) {
    fun validated(): ToolDefinition {
        require(id.isNotBlank()) { "Tool id must not be blank." }
        require(name.isNotBlank()) { "Tool name must not be blank." }
        require(category.isNotBlank()) { "Tool category must not be blank." }
        return copy(
            id = id.trim(),
            name = name.trim(),
            description = description.trim(),
            category = category.trim(),
            keywords = keywords.trim(),
        )
    }

    fun toJson(): JSONObject = JSONObject()
        .put("id", id)
        .put("name", name)
        .put("description", description)
        .put("category", category)
        .put("scope", scope.name)
        .put("keywords", keywords)
        .put("argumentsSchema", argumentsSchema.toJson())
        .put("resultSchema", resultSchema.toJson())
        .put("security", security.toJson())
}

data class AndroidToolResult(
    val success: Boolean,
    val message: String? = null,
    val errorCode: String? = null,
    val payload: JSONObject? = null,
) {
    companion object {
        fun success(payload: JSONObject? = null, message: String? = null) = AndroidToolResult(true, message, payload = payload)
        fun failure(message: String, errorCode: String? = null, payload: JSONObject? = null) =
            AndroidToolResult(false, message, errorCode, payload)
    }
}

class AndroidToolExecutionContext(
    val application: android.app.Application,
    val transport: PairingLiveSessionTransport?,
    val sessionId: String?,
    val requestId: String?,
    val options: AnsightOptions,
)

interface AndroidTool {
    val definition: ToolDefinition
    fun execute(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult
}

class FunctionAndroidTool(
    override val definition: ToolDefinition,
    private val handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) : AndroidTool {
    override fun execute(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult =
        handler(arguments, context)
}

class AndroidToolRegistry(tools: Iterable<AndroidTool> = emptyList()) {
    private val toolsById = linkedMapOf<String, AndroidTool>()

    init {
        tools.forEach { tool -> register(tool) }
    }

    val size: Int
        get() = toolsById.size

    fun register(tool: AndroidTool, replaceExisting: Boolean = false) {
        val validated = tool.definition.validated()
        require(replaceExisting || validated.id !in toolsById) { "A tool with id '${validated.id}' is already registered." }
        toolsById[validated.id] = if (validated == tool.definition) {
            tool
        } else {
            object : AndroidTool {
                override val definition: ToolDefinition = validated

                override fun execute(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult =
                    tool.execute(arguments, context)
            }
        }
    }

    fun clear() {
        toolsById.clear()
    }

    fun all(): List<AndroidTool> = toolsById.values.toList()

    fun get(id: String): AndroidTool? = toolsById[id.trim()]

    fun contains(id: String): Boolean = id.trim() in toolsById

    fun visible(guard: AnsightToolGuard): List<AndroidTool> = all().filter { guard.canDiscover(it.definition.scope) }
}

internal fun AnsightToolGuard.canDiscover(scope: ToolScope): Boolean = when (this) {
    AnsightToolGuard.Disabled -> false
    AnsightToolGuard.ReadOnly -> scope == ToolScope.Read
    AnsightToolGuard.ReadWrite -> scope == ToolScope.Read || scope == ToolScope.Write
    AnsightToolGuard.FullAccess -> true
}

internal fun AnsightToolGuard.canExecute(scope: ToolScope): Boolean = canDiscover(scope)

internal fun String.toToolScope(): ToolScope = when (trim().toLowerCase(Locale.US)) {
    "write" -> ToolScope.Write
    "delete" -> ToolScope.Delete
    else -> ToolScope.Read
}

internal fun AnsightToolGuard.toProtocolJson(): JSONObject {
    val scopes = when (this) {
        AnsightToolGuard.Disabled -> emptyList()
        AnsightToolGuard.ReadOnly -> listOf(ToolScope.Read.name)
        AnsightToolGuard.ReadWrite -> listOf(ToolScope.Read.name, ToolScope.Write.name)
        AnsightToolGuard.FullAccess -> ToolScope.values().map { it.name }
    }
    return JSONObject()
        .put("discoveryEnabled", this != AnsightToolGuard.Disabled)
        .put("executionEnabled", this != AnsightToolGuard.Disabled)
        .put("allowedScopes", JSONArray(scopes))
}

object ToolProtocol {
    const val Capability = "tool.exec"
    const val QueryType = "tool.query"
    const val CatalogType = "tool.catalog"
    const val CallType = "tool.call"
    const val ResultType = "tool.result"
    const val ErrorType = "tool.error"
}
