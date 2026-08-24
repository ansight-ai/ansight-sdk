package ai.ansight.runtime

fun androidUiTool(
    id: String,
    name: String,
    description: String,
    scope: ToolScope = ToolScope.Read,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(id, name, description, "ui", scope, "ui android view screenshot overlay", handler)

fun androidJsonUiTool(
    definition: ToolDefinition,
    handler: (org.json.JSONObject, AndroidToolExecutionContext) -> AndroidToolResult,
): AndroidTool = FunctionJsonAndroidTool(definition, handler = handler)

fun androidFileTool(
    id: String,
    name: String,
    description: String,
    scope: ToolScope = ToolScope.Read,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(id, name, description, "files", scope, "files sandbox app data cache", handler)

fun androidPreferencesTool(
    id: String,
    name: String,
    description: String,
    scope: ToolScope = ToolScope.Read,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(id, name, description, "prefs", scope, "preferences sharedpreferences settings", handler)

fun androidSecureStorageTool(
    id: String,
    name: String,
    description: String,
    scope: ToolScope = ToolScope.Read,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(
    id,
    name,
    description,
    "secure",
    scope,
    "secure storage keystore allow-list",
    handler,
    ToolSecurity(ToolSecurityLevel.Critical, listOf("AccessesSecureStorage")),
)

fun androidDatabaseTool(
    id: String,
    name: String,
    description: String,
    scope: ToolScope = ToolScope.Read,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
) = androidSimpleTool(
    id,
    name,
    description,
    "data",
    scope,
    "sqlite database query schema",
    handler,
    ToolSecurity(ToolSecurityLevel.High, listOf("AccessesDatabase")),
)

fun androidReflectionTool(
    id: String,
    name: String,
    description: String,
    scope: ToolScope,
    security: ToolSecurity,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
): AndroidTool = androidSimpleTool(
    id = id,
    name = name,
    description = description,
    category = "reflect",
    scope = scope,
    keywords = "reflection android runtime object state fields methods",
    handler = handler,
    security = security,
)

fun androidSimpleTool(
    id: String,
    name: String,
    description: String,
    category: String,
    scope: ToolScope,
    keywords: String,
    handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
    security: ToolSecurity = ToolSecurity.Unspecified,
): AndroidTool = FunctionAndroidTool(
    ToolDefinition(
        id = id,
        name = name,
        description = description,
        category = category,
        scope = scope,
        keywords = keywords,
        argumentsSchema = ToolSchema.obj(additionalProperties = true),
        resultSchema = ToolSchema.obj(additionalProperties = true),
        security = security,
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
