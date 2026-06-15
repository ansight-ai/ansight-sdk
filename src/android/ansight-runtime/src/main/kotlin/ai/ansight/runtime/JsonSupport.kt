package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject

internal fun JSONObject.putNullable(name: String, value: Any?): JSONObject {
    if (value == null) {
        put(name, JSONObject.NULL)
    } else {
        put(name, value)
    }
    return this
}

internal fun JSONObject.putIfNotNull(name: String, value: Any?): JSONObject {
    if (value != null) {
        put(name, value)
    }
    return this
}

internal fun Map<String, Map<String, String>>.toJSONObject(): JSONObject {
    val result = JSONObject()
    entries.sortedBy { it.key }.forEach { group ->
        val groupObject = JSONObject()
        group.value.entries.sortedBy { it.key }.forEach { entry ->
            groupObject.put(entry.key, entry.value)
        }
        result.put(group.key, groupObject)
    }
    return result
}

internal fun Iterable<Any>.toJSONArray(): JSONArray {
    val array = JSONArray()
    forEach { array.put(it) }
    return array
}

internal fun JSONObject.requiredString(name: String): String {
    val value = optString(name, "").trim()
    require(value.isNotEmpty()) { "Pairing config field '$name' is required." }
    return value
}

internal fun JSONObject.optionalString(name: String): String? {
    if (!has(name) || isNull(name)) {
        return null
    }

    return optString(name, "").trim().ifBlank { null }
}

internal fun JSONObject.optionalInt(name: String): Int? {
    if (!has(name) || isNull(name)) {
        return null
    }

    return optInt(name)
}

internal fun JSONObject.optionalBoolean(name: String): Boolean? {
    if (!has(name) || isNull(name)) {
        return null
    }

    return optBoolean(name)
}
