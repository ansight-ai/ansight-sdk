package ai.ansight.tools.preferences

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.ToolScope
import ai.ansight.runtime.androidPreferencesTool
import ai.ansight.runtime.putNullable
import android.app.Application
import android.content.SharedPreferences
import org.json.JSONArray
import org.json.JSONObject

object AndroidPreferencesTools {
    fun create(): List<AndroidTool> = listOf(
        androidPreferencesTool(
            PreferencesToolIds.ListKeys,
            "List Preference Keys",
            "Lists keys from SharedPreferences.",
        ) { args, context ->
            val prefs = preferences(context.application, args)
            AndroidToolResult.success(
                JSONObject()
                    .put("name", prefs.first)
                    .put("keys", JSONArray(prefs.second.all.keys.sorted()))
                    .put("count", prefs.second.all.size),
            )
        },
        androidPreferencesTool(
            PreferencesToolIds.GetValue,
            "Get Preference Value",
            "Reads one SharedPreferences value.",
        ) { args, context ->
            val key = args["key"] ?: return@androidPreferencesTool AndroidToolResult.failure(
                "Preference key is required.",
                "preference_key_required",
            )
            val prefs = preferences(context.application, args)
            AndroidToolResult.success(
                JSONObject()
                    .put("name", prefs.first)
                    .put("key", key)
                    .putNullable("value", prefs.second.all[key]?.toString())
                    .put("exists", prefs.second.all.containsKey(key)),
            )
        },
        androidPreferencesTool(
            PreferencesToolIds.SetValue,
            "Set Preference Value",
            "Writes one SharedPreferences string value.",
            ToolScope.Write,
        ) { args, context ->
            val key = args["key"] ?: return@androidPreferencesTool AndroidToolResult.failure(
                "Preference key is required.",
                "preference_key_required",
            )
            val value = args["value"] ?: ""
            val prefs = preferences(context.application, args)
            prefs.second.edit().putString(key, value).apply()
            AndroidToolResult.success(JSONObject().put("name", prefs.first).put("key", key).put("written", true))
        },
        androidPreferencesTool(
            PreferencesToolIds.RemoveKey,
            "Remove Preference Key",
            "Removes one SharedPreferences key.",
            ToolScope.Delete,
        ) { args, context ->
            val key = args["key"] ?: return@androidPreferencesTool AndroidToolResult.failure(
                "Preference key is required.",
                "preference_key_required",
            )
            val prefs = preferences(context.application, args)
            prefs.second.edit().remove(key).apply()
            AndroidToolResult.success(JSONObject().put("name", prefs.first).put("key", key).put("removed", true))
        },
    )

    private fun preferences(application: Application, args: Map<String, String>): Pair<String, SharedPreferences> {
        val name = args["name"]?.trim()?.ifBlank { null } ?: "${application.packageName}_preferences"
        return name to application.getSharedPreferences(name, Application.MODE_PRIVATE)
    }
}
