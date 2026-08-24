package ai.ansight.runtime

fun androidUiTool(
    id: String,
    name: String,
    description: String,
    policy: ToolPolicy = ToolPolicy.Read,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(id, name, description, "ui", policy, "ui android view screenshot overlay", handler)

fun androidJsonUiTool(
    definition: ToolDefinition,
    handler: (org.json.JSONObject, AndroidToolExecutionContext) -> AndroidToolResult,
): AndroidTool = FunctionJsonAndroidTool(definition, handler = handler)

fun androidFileTool(
    id: String,
    name: String,
    description: String,
    policy: ToolPolicy = ToolPolicy.Read,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(id, name, description, "files", policy, "files sandbox app data cache", handler)

fun androidPreferencesTool(
    id: String,
    name: String,
    description: String,
    policy: ToolPolicy = ToolPolicy.Read,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(id, name, description, "prefs", policy, "preferences sharedpreferences settings", handler)

fun androidSecureStorageTool(
    id: String,
    name: String,
    description: String,
    policy: ToolPolicy = ToolPolicy.Critical,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(
    id,
    name,
    description,
    "secure",
    policy,
    "secure storage keystore allow-list",
    handler,
)

fun androidDatabaseTool(
    id: String,
    name: String,
    description: String,
    policy: ToolPolicy = ToolPolicy.Read,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(
    id,
    name,
    description,
    "data",
    policy,
    "sqlite database query schema",
    handler,
)

fun androidReflectionTool(
    id: String,
    name: String,
    description: String,
    policy: ToolPolicy,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
): AndroidTool = androidSimpleTool(
    id = id,
    name = name,
    description = description,
    category = "reflect",
    policy = policy,
    keywords = "reflection android runtime object state fields methods",
    handler = handler,
)

fun androidSimpleTool(
    id: String,
    name: String,
    description: String,
    category: String,
    policy: ToolPolicy,
    keywords: String,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
): AndroidTool = FunctionAndroidTool(
    ToolDefinition(
        id = id,
        name = name,
        description = description,
        category = category,
        policy = policy,
        keywords = keywords,
        argumentsSchema = ToolSchema.obj(additionalProperties = true),
        resultSchema = ToolSchema.obj(additionalProperties = true),
    ),
    handler = handler,
)

fun Map<String, String>.intArg(name: String, defaultValue: Int): Int = this[name]?.toIntOrNull() ?: defaultValue

fun Map<String, String>.booleanArg(name: String, defaultValue: Boolean): Boolean {
    return when (this[name]?.trim()) {
        "true" -> true
        "false" -> false
        else -> defaultValue
    }
}
