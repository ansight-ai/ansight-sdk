package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale

enum class ToolPolicy {
    Read,
    Write,
    Critical;

    val wireName: String
        get() = name.lowercase(Locale.US)
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
    val policy: ToolPolicy,
    val keywords: String,
    val argumentsSchema: ToolSchema = ToolSchema.obj(additionalProperties = true),
    val resultSchema: ToolSchema = ToolSchema.obj(additionalProperties = true),
    val prerequisiteToolIds: List<String> = emptyList(),
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
            prerequisiteToolIds = prerequisiteToolIds
                .map { it.trim() }
                .filter { it.isNotEmpty() }
                .distinct()
                .sorted(),
        )
    }

    fun toJson(): JSONObject = JSONObject().apply {
        put("id", id)
        put("name", name)
        put("description", description)
        put("category", category)
        put("policy", policy.wireName)
        if (keywords.isNotBlank()) put("keywords", keywords)
        put("argumentsSchema", argumentsSchema.toProtocolJson())
        put("resultSchema", resultSchema.toProtocolJson())
        if (prerequisiteToolIds.isNotEmpty()) {
            put("prerequisiteToolIds", JSONArray(prerequisiteToolIds))
        }
    }
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

data class ToolAvailability(
    val available: Boolean,
    val reasonCode: String? = null,
    val reason: String? = null,
    val requiredState: String? = null,
    val remediation: String? = null,
    val retryable: Boolean = false,
) {
    fun toJson(): JSONObject = JSONObject().apply {
        put("available", available)
        reasonCode?.takeIf { it.isNotBlank() }?.let { put("code", it) }
        reason?.takeIf { it.isNotBlank() }?.let { put("reason", it) }
        requiredState?.takeIf { it.isNotBlank() }?.let { put("requiredState", it) }
        remediation?.takeIf { it.isNotBlank() }?.let { put("remediation", it) }
        if (retryable) put("retryable", true)
    }

    companion object {
        val Available = ToolAvailability(true)

        fun unavailable(
            reasonCode: String,
            reason: String,
            requiredState: String? = null,
            remediation: String? = null,
            retryable: Boolean = true,
        ) = ToolAvailability(false, reasonCode, reason, requiredState, remediation, retryable)
    }
}

class AndroidToolExecutionContext(
    val application: android.app.Application,
    val transport: PairingLiveSessionTransport?,
    val sessionId: String?,
    val requestId: String?,
    val options: AnsightOptions,
    internal val pendingBinaryTransfers: PendingBinaryTransferQueue = PendingBinaryTransferQueue(),
)

interface AndroidTool {
    val definition: ToolDefinition
    fun availability(context: AndroidToolExecutionContext): ToolAvailability = ToolAvailability.Available
    fun execute(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult
}

/** A tool that receives protocol arguments as structured JSON. */
interface JsonAndroidTool : AndroidTool {
    fun executeJson(arguments: JSONObject, context: AndroidToolExecutionContext): AndroidToolResult

    override fun execute(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult {
        val json = JSONObject()
        arguments.forEach { (key, value) ->
            json.put(key, runCatching { org.json.JSONTokener(value).nextValue() }.getOrDefault(value))
        }
        return executeJson(json, context)
    }
}

fun interface ExternalToolProtocolHandler {
    fun handle(messageJson: String): String?
}

class FunctionAndroidTool(
    override val definition: ToolDefinition,
    private val availabilityHandler: (AndroidToolExecutionContext) -> ToolAvailability = { ToolAvailability.Available },
    private val handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) : AndroidTool {
    override fun availability(context: AndroidToolExecutionContext): ToolAvailability = availabilityHandler(context)

    override fun execute(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult =
        handler(arguments, context)
}

class FunctionJsonAndroidTool(
    override val definition: ToolDefinition,
    private val availabilityHandler: (AndroidToolExecutionContext) -> ToolAvailability = { ToolAvailability.Available },
    private val handler: (JSONObject, AndroidToolExecutionContext) -> AndroidToolResult,
) : JsonAndroidTool {
    override fun availability(context: AndroidToolExecutionContext): ToolAvailability = availabilityHandler(context)

    override fun executeJson(arguments: JSONObject, context: AndroidToolExecutionContext): AndroidToolResult =
        handler(arguments, context)
}

data class ToolSchemaValidationError(
    val path: String,
    val code: String,
    val message: String,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("path", path)
        .put("code", code)
        .put("message", message)
}

object ToolSchemaValidator {
    fun validate(schema: ToolSchema, value: Any?): List<ToolSchemaValidationError> {
        val errors = mutableListOf<ToolSchemaValidationError>()
        validateValue(schema, value, "$", errors)
        return errors
    }

    private fun validateValue(
        schema: ToolSchema,
        value: Any?,
        path: String,
        errors: MutableList<ToolSchemaValidationError>,
    ) {
        if (value == null || value == JSONObject.NULL) {
            if (!schema.nullable) {
                errors += ToolSchemaValidationError(path, "null_not_allowed", "The value cannot be null.")
            }
            return
        }

        when (schema.type) {
            "object" -> validateObject(schema, value, path, errors)
            "array" -> validateArray(schema, value, path, errors)
            "string" -> {
                if (value !is String) {
                    typeError(path, "string", errors)
                } else if (schema.enumValues.isNotEmpty() && value !in schema.enumValues) {
                    errors += ToolSchemaValidationError(path, "enum_value_invalid", "The value is not in the declared enum.")
                }
            }
            "integer" -> if (value !is Byte && value !is Short && value !is Int && value !is Long) {
                typeError(path, "integer", errors)
            }
            "number" -> if (value !is Number) typeError(path, "number", errors)
            "boolean" -> if (value !is Boolean) typeError(path, "boolean", errors)
        }
    }

    private fun validateObject(
        schema: ToolSchema,
        value: Any,
        path: String,
        errors: MutableList<ToolSchemaValidationError>,
    ) {
        if (value !is JSONObject) {
            typeError(path, "object", errors)
            return
        }
        schema.required.forEach { name ->
            if (!value.has(name) || value.isNull(name)) {
                errors += ToolSchemaValidationError("$path.$name", "required_property_missing", "The required property '$name' is missing.")
            }
        }
        value.keys().forEach { name ->
            val propertySchema = schema.properties[name]
            if (propertySchema == null) {
                if (!schema.additionalProperties) {
                    errors += ToolSchemaValidationError("$path.$name", "additional_property_not_allowed", "The property '$name' is not declared by the schema.")
                }
            } else {
                validateValue(propertySchema, value.opt(name), "$path.$name", errors)
            }
        }
    }

    private fun validateArray(
        schema: ToolSchema,
        value: Any,
        path: String,
        errors: MutableList<ToolSchemaValidationError>,
    ) {
        if (value !is JSONArray) {
            typeError(path, "array", errors)
            return
        }
        schema.items?.let { itemSchema ->
            for (index in 0 until value.length()) {
                validateValue(itemSchema, value.opt(index), "$path[$index]", errors)
            }
        }
    }

    private fun typeError(
        path: String,
        expected: String,
        errors: MutableList<ToolSchemaValidationError>,
    ) {
        errors += ToolSchemaValidationError(path, "type_mismatch", "The value must be a JSON $expected.")
    }

    fun errorsJson(errors: List<ToolSchemaValidationError>): JSONObject = JSONObject()
        .put("valid", errors.isEmpty())
        .put("errors", JSONArray(errors.map { it.toJson() }))
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

                override fun availability(context: AndroidToolExecutionContext): ToolAvailability =
                    tool.availability(context)

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

    fun visible(guard: AnsightToolGuard): List<AndroidTool> = all().filter { guard.canDiscover(it.definition.policy) }
}

internal fun AnsightToolGuard.canDiscover(policy: ToolPolicy): Boolean = when (this) {
    AnsightToolGuard.Disabled -> false
    AnsightToolGuard.ReadOnly -> policy <= ToolPolicy.Read
    AnsightToolGuard.ReadWrite -> policy <= ToolPolicy.Write
    AnsightToolGuard.FullAccess -> true
}

internal fun AnsightToolGuard.canExecute(policy: ToolPolicy): Boolean = canDiscover(policy)

internal fun String.toToolPolicy(): ToolPolicy = when (trim().lowercase(Locale.US)) {
    "write" -> ToolPolicy.Write
    "critical", "delete" -> ToolPolicy.Critical
    else -> ToolPolicy.Read
}

internal fun AnsightToolGuard.toProtocolJson(): JSONObject {
    val maxPolicy = when (this) {
        AnsightToolGuard.Disabled, AnsightToolGuard.ReadOnly -> ToolPolicy.Read
        AnsightToolGuard.ReadWrite -> ToolPolicy.Write
        AnsightToolGuard.FullAccess -> ToolPolicy.Critical
    }
    return JSONObject()
        .put("discoveryEnabled", this != AnsightToolGuard.Disabled)
        .put("executionEnabled", this != AnsightToolGuard.Disabled)
        .put("maxPolicy", maxPolicy.wireName)
}

object ToolProtocol {
    const val Capability = "tool.exec"
    const val QueryType = "tool.query"
    const val CatalogType = "tool.catalog"
    const val CallType = "tool.call"
    const val BatchType = "tool.batch"
    const val ResultType = "tool.result"
    const val BatchResultType = "tool.batch.result"
    const val ErrorType = "tool.error"
    const val CatalogSchema = "ansight.tool-catalog.v3"
    const val FullCatalogDetail = "full"
    const val IndexCatalogDetail = "index"
    const val DefinitionsCatalogDetail = "definitions"
}

private fun ToolSchema.toProtocolJson(): JSONObject {
    val json = JSONObject().put("type", if (nullable) JSONArray(listOf(type, "null")) else type)
    if (additionalProperties) json.put("additionalProperties", true)
    json.putIfNotNull("description", description)
    json.putIfNotNull("format", format)
    if (properties.isNotEmpty()) {
        val propertyJson = JSONObject()
        properties.entries.sortedBy { it.key }.forEach { propertyJson.put(it.key, it.value.toProtocolJson()) }
        json.put("properties", propertyJson)
    }
    if (required.isNotEmpty()) json.put("required", JSONArray(required))
    if (items != null) json.put("items", items.toProtocolJson())
    if (enumValues.isNotEmpty()) json.put("enum", JSONArray(enumValues))
    return json
}
