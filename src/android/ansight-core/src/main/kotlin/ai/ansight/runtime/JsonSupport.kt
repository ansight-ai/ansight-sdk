package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject

fun JSONObject.putNullable(name: String, value: Any?): JSONObject {
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

internal fun Map<String, Map<String, String>>.normalizedCustomProperties(): Map<String, Map<String, String>> {
    return mapKeys { it.key.trim() }
        .filterKeys { it.isNotBlank() }
        .mapValues { group ->
            group.value
                .mapKeys { it.key.trim() }
                .filterKeys { it.isNotBlank() }
                .mapValues { it.value.trim() }
        }
        .filterValues { it.isNotEmpty() }
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

internal fun JSONObject.stringList(name: String): List<String> {
    val values = optJSONArray(name) ?: return emptyList()
    return buildList {
        for (index in 0 until values.length()) {
            values.optString(index).trim().ifBlank { null }?.let(::add)
        }
    }
}

internal fun <T> JSONObject.objectList(name: String, parse: (JSONObject) -> T): List<T> {
    val values = optJSONArray(name) ?: return emptyList()
    return buildList {
        for (index in 0 until values.length()) {
            values.optJSONObject(index)?.let(parse)?.let(::add)
        }
    }
}
