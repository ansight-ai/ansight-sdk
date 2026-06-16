package ai.ansight.tools.reflection

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AndroidUiEvidence
import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.ToolScope
import ai.ansight.runtime.ToolSecurity
import ai.ansight.runtime.ToolSecurityLevel
import ai.ansight.runtime.androidReflectionTool
import ai.ansight.runtime.intArg
import ai.ansight.runtime.putNullable
import org.json.JSONArray
import org.json.JSONObject
import java.lang.reflect.Field
import java.lang.reflect.Method
import java.lang.reflect.Modifier

object AndroidReflectionTools {
    fun create(): List<AndroidTool> = listOf(
        androidReflectionTool(
            ReflectionToolIds.ListRoots,
            "List Reflection Roots",
            "Lists registered Android runtime object roots available for reflection tools.",
            ToolScope.Read,
            ToolSecurity(ToolSecurityLevel.High, listOf("MetadataDisclosure", "InspectsRuntimeState")),
        ) { _, context ->
            AndroidToolResult.success(JSONObject().put("roots", JSONArray(roots(context).map { it.toJson() })))
        },
        androidReflectionTool(
            ReflectionToolIds.InspectObject,
            "Inspect Object",
            "Inspects a registered Android object root and returns an expandable snapshot.",
            ToolScope.Read,
            ToolSecurity(ToolSecurityLevel.Critical, listOf("ReadsAppData", "InspectsRuntimeState")),
        ) { args, context ->
            val target = resolveTarget(context, args)
            AndroidToolResult.success(JSONObject().put("snapshot", snapshot(target.value, target.rootId, target.path, args.intArg("maxDepth", 1))))
        },
        androidReflectionTool(
            ReflectionToolIds.DescribeType,
            "Describe Type",
            "Returns runtime type metadata without reading live object values.",
            ToolScope.Read,
            ToolSecurity(ToolSecurityLevel.Medium, listOf("MetadataDisclosure")),
        ) { args, context ->
            val type = args["type"]?.trim()?.ifBlank { null }?.let { Class.forName(it) }
                ?: resolveTarget(context, args).value?.javaClass
                ?: return@androidReflectionTool AndroidToolResult.failure("No type or live object was resolved.", "reflect_type_required")
            describeType(type)
        },
        androidReflectionTool(
            ReflectionToolIds.SetMemberValue,
            "Set Member Value",
            "Writes a simple writable field or JavaBean property reachable from a registered Android object root.",
            ToolScope.Write,
            ToolSecurity(ToolSecurityLevel.Critical, listOf("WritesAppData", "MutatesRuntimeState")),
        ) { args, context ->
            val member = args["member"] ?: args["name"] ?: return@androidReflectionTool AndroidToolResult.failure("Member name is required.", "reflect_member_required")
            val target = resolveTarget(context, args)
            val receiver = target.value ?: return@androidReflectionTool AndroidToolResult.failure("Cannot set a member on null.", "reflect_null_target")
            val rawValue = args["value"] ?: return@androidReflectionTool AndroidToolResult.failure("Value is required.", "reflect_value_required")
            writeMember(receiver, member, rawValue)
        },
        androidReflectionTool(
            ReflectionToolIds.InvokeMethod,
            "Invoke Method",
            "Invokes a no-argument instance method reachable from a registered Android object root.",
            ToolScope.Write,
            ToolSecurity(ToolSecurityLevel.Critical, listOf("InvokesAppCode", "MutatesRuntimeState")),
        ) { args, context ->
            val methodName = args["method"] ?: args["name"] ?: return@androidReflectionTool AndroidToolResult.failure("Method name is required.", "reflect_method_required")
            val target = resolveTarget(context, args)
            val receiver = target.value ?: return@androidReflectionTool AndroidToolResult.failure("Cannot invoke a method on null.", "reflect_null_target")
            invokeNoArgMethod(receiver, methodName, target.rootId, target.path)
        },
    )

    private fun roots(context: AndroidToolExecutionContext): List<ReflectionRoot> {
        val builtInRoots = listOf(
            ReflectionRoot(
                id = "application",
                value = context.application,
                displayName = "Android Application",
                description = context.application.javaClass.name,
                referenceType = "strong",
            ),
            ReflectionRoot(
                id = "activity",
                value = AndroidUiEvidence.currentActivity(),
                displayName = "Current Activity",
                description = AndroidUiEvidence.currentActivity()?.javaClass?.name ?: "No resumed activity is available.",
                referenceType = "weak",
            ),
            ReflectionRoot(
                id = "runtime.snapshot",
                value = AnsightRuntime.snapshot(),
                displayName = "Ansight Runtime Snapshot",
                description = "Current SDK runtime state.",
                referenceType = "strong",
            ),
        )
        val registeredRoots = AndroidReflectionRootRegistry.snapshot().map { root ->
            val resolved = runCatching { root.resolve() }.getOrNull()
            ReflectionRoot(
                id = root.id,
                value = resolved,
                displayName = root.displayName,
                description = root.description ?: resolved?.javaClass?.name ?: "Registered root is unavailable.",
                referenceType = root.referenceType,
            )
        }
        return builtInRoots + registeredRoots
    }

    private fun resolveTarget(context: AndroidToolExecutionContext, args: Map<String, String>): ReflectionTarget {
        val rootId = args["root"] ?: args["rootId"] ?: "application"
        val root = roots(context).firstOrNull { it.id == rootId }
            ?: throw IllegalArgumentException("Unknown reflection root '$rootId'.")
        val path = args["path"]?.trim()?.ifBlank { null }
        var value = root.value ?: throw IllegalArgumentException("Reflection root '$rootId' is unavailable.")
        if (path != null) {
            for (segment in path.split('.').filter { it.isNotBlank() }) {
                value = readMember(value, segment)
                    ?: throw IllegalArgumentException("Path segment '$segment' resolved to null.")
            }
        }
        return ReflectionTarget(rootId, path, value)
    }

    private fun snapshot(value: Any?, rootId: String, path: String?, maxDepth: Int): JSONObject {
        val json = valueSummary(value)
            .put("root", rootId)
            .putNullable("path", path)
        if (value == null || maxDepth <= 0 || isScalar(value.javaClass)) {
            return json
        }
        json.put("members", JSONArray(readableFields(value.javaClass).take(64).map { field ->
            val memberValue = runCatching {
                field.isAccessible = true
                field.get(value)
            }.getOrNull()
            JSONObject()
                .put("name", field.name)
                .put("declaringType", field.declaringClass.name)
                .put("writable", !Modifier.isFinal(field.modifiers))
                .put("value", snapshot(memberValue, rootId, joinPath(path, field.name), maxDepth - 1))
        }))
        json.put("methods", JSONArray(value.javaClass.methods
            .filter { method -> method.parameterTypes.isEmpty() && method.declaringClass != Object::class.java }
            .sortedBy { it.name }
            .take(64)
            .map { method ->
                JSONObject()
                    .put("name", method.name)
                    .put("signature", methodSignature(method))
                    .put("returnType", method.returnType.name)
                    .put("invokable", true)
            }))
        return json
    }

    private fun describeType(type: Class<*>): AndroidToolResult {
        val fields = JSONArray(readableFields(type).take(128).map { field ->
            JSONObject()
                .put("name", field.name)
                .put("type", field.type.name)
                .put("declaringType", field.declaringClass.name)
                .put("static", Modifier.isStatic(field.modifiers))
                .put("writable", !Modifier.isFinal(field.modifiers))
        })
        val methods = JSONArray(type.methods
            .filter { it.declaringClass != Object::class.java }
            .sortedBy { it.name }
            .take(128)
            .map { method ->
                JSONObject()
                    .put("name", method.name)
                    .put("signature", methodSignature(method))
                    .put("returnType", method.returnType.name)
                    .put("parameterCount", method.parameterTypes.size)
            })
        return AndroidToolResult.success(JSONObject()
            .put("type", type.name)
            .put("simpleName", type.simpleName)
            .putNullable("packageName", type.`package`?.name)
            .put("fields", fields)
            .put("methods", methods))
    }

    private fun writeMember(receiver: Any, member: String, rawValue: String): AndroidToolResult {
        val field = findField(receiver.javaClass, member)
        if (field != null) {
            field.isAccessible = true
            if (Modifier.isFinal(field.modifiers)) {
                return AndroidToolResult.failure("Field '$member' is final.", "reflect_member_not_writable")
            }
            field.set(receiver, convertValue(rawValue, field.type))
            return AndroidToolResult.success(JSONObject().put("member", member).put("written", true))
        }

        val setterName = "set" + member.capitalizedMemberName()
        val setter = receiver.javaClass.methods.firstOrNull { it.name == setterName && it.parameterTypes.size == 1 }
            ?: return AndroidToolResult.failure("Writable member '$member' was not found.", "reflect_member_not_found")
        setter.invoke(receiver, convertValue(rawValue, setter.parameterTypes[0]))
        return AndroidToolResult.success(JSONObject().put("member", member).put("written", true).put("setter", setter.name))
    }

    private fun invokeNoArgMethod(receiver: Any, methodName: String, rootId: String, path: String?): AndroidToolResult {
        val method = receiver.javaClass.methods.firstOrNull { it.name == methodName && it.parameterTypes.isEmpty() }
            ?: return AndroidToolResult.failure("No-argument method '$methodName' was not found.", "reflect_method_not_found")
        method.isAccessible = true
        val result = method.invoke(receiver)
        return AndroidToolResult.success(JSONObject()
            .put("method", methodName)
            .put("returnSnapshot", snapshot(result, rootId, joinPath(path, "$methodName()"), 1)))
    }

    private fun readMember(receiver: Any, segment: String): Any? {
        val (name, index) = parseSegment(segment)
        val value = if (name.isEmpty()) receiver else findField(receiver.javaClass, name)?.let { field ->
            field.isAccessible = true
            field.get(receiver)
        } ?: receiver.javaClass.methods.firstOrNull {
            it.parameterTypes.isEmpty() && (it.name == name || it.name == "get" + name.capitalizedMemberName())
        }
            ?.let { method ->
                method.isAccessible = true
                method.invoke(receiver)
            }
        return if (index == null) value else indexedValue(value, index)
    }

    private fun parseSegment(segment: String): Pair<String, Int?> {
        val bracket = segment.indexOf('[')
        if (bracket < 0 || !segment.endsWith("]")) {
            return segment to null
        }
        return segment.substring(0, bracket) to segment.substring(bracket + 1, segment.length - 1).toIntOrNull()
    }

    private fun indexedValue(value: Any?, index: Int?): Any? {
        if (value == null || index == null) {
            return null
        }
        return when (value) {
            is List<*> -> value.getOrNull(index)
            is Array<*> -> value.getOrNull(index)
            is Iterable<*> -> value.drop(index).firstOrNull()
            else -> if (value.javaClass.isArray) java.lang.reflect.Array.get(value, index) else null
        }
    }

    private fun readableFields(type: Class<*>): List<Field> {
        val fields = mutableListOf<Field>()
        var current: Class<*>? = type
        while (current != null && current != Object::class.java) {
            fields.addAll(current.declaredFields.filter { !it.isSynthetic }.sortedBy { it.name })
            current = current.superclass
        }
        return fields
    }

    private fun findField(type: Class<*>, name: String): Field? {
        var current: Class<*>? = type
        while (current != null && current != Object::class.java) {
            current.declaredFields.firstOrNull { it.name == name }?.let { return it }
            current = current.superclass
        }
        return null
    }

    private fun valueSummary(value: Any?): JSONObject {
        if (value == null) {
            return JSONObject()
                .put("kind", "null")
                .putNullable("runtimeType", null)
                .putNullable("preview", null)
        }
        val type = value.javaClass
        val json = JSONObject()
            .put("kind", if (isScalar(type)) "scalar" else if (value is Iterable<*> || type.isArray) "collection" else "object")
            .put("runtimeType", type.name)
            .put("preview", preview(value))
        if (isScalar(type)) {
            json.put("value", value)
        }
        return json
    }

    private fun preview(value: Any?): String? {
        if (value == null) {
            return null
        }
        val text = value.toString()
        return if (text.length <= 160) text else text.substring(0, 157) + "..."
    }

    private fun isScalar(type: Class<*>): Boolean {
        return type.isPrimitive ||
            Number::class.java.isAssignableFrom(type) ||
            type == java.lang.Boolean::class.java ||
            type == java.lang.String::class.java ||
            type == java.lang.Character::class.java ||
            type.isEnum
    }

    private fun convertValue(value: String, type: Class<*>): Any? {
        return when (type) {
            java.lang.String::class.java -> value
            java.lang.Boolean.TYPE, java.lang.Boolean::class.java -> value.toBoolean()
            java.lang.Integer.TYPE, java.lang.Integer::class.java -> value.toInt()
            java.lang.Long.TYPE, java.lang.Long::class.java -> value.toLong()
            java.lang.Float.TYPE, java.lang.Float::class.java -> value.toFloat()
            java.lang.Double.TYPE, java.lang.Double::class.java -> value.toDouble()
            java.lang.Short.TYPE, java.lang.Short::class.java -> value.toShort()
            java.lang.Byte.TYPE, java.lang.Byte::class.java -> value.toByte()
            java.lang.Character.TYPE, java.lang.Character::class.java -> value.firstOrNull()
            else -> if (type.isEnum) enumValue(type, value) else throw IllegalArgumentException("Unsupported writable value type '${type.name}'.")
        }
    }

    @Suppress("UNCHECKED_CAST")
    private fun enumValue(type: Class<*>, value: String): Any? {
        return java.lang.Enum.valueOf(type as Class<out Enum<*>>, value)
    }

    private fun methodSignature(method: Method): String {
        return method.name + "(" + method.parameterTypes.joinToString(",") { it.simpleName } + ")"
    }

    private fun joinPath(path: String?, segment: String): String = if (path.isNullOrBlank()) segment else "$path.$segment"

    private fun String.capitalizedMemberName(): String = replaceFirstChar { char ->
        if (char.isLowerCase()) char.titlecase() else char.toString()
    }

    private data class ReflectionRoot(
        val id: String,
        val value: Any?,
        val displayName: String,
        val description: String,
        val referenceType: String,
    ) {
        fun toJson(): JSONObject = JSONObject()
            .put("id", id)
            .put("available", value != null)
            .put("referenceType", referenceType)
            .put("type", value?.javaClass?.name ?: JSONObject.NULL)
            .put("metadata", JSONObject()
                .put("displayName", displayName)
                .put("description", description))
    }

    private data class ReflectionTarget(val rootId: String, val path: String?, val value: Any?)
}
