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
    @JvmStatic
    @JvmOverloads
    fun create(options: AndroidPreferencesToolsOptions = AndroidPreferencesToolsOptions.Default): List<AndroidTool> = listOf(
        androidPreferencesTool(
            PreferencesToolIds.ListKeys,
            "List Preference Keys",
            "Lists keys from SharedPreferences.",
        ) { args, context ->
            val prefs = preferences(context.application, args, options)
            val keys = prefs.second.all.keys
                .filter { options.isKeyAllowed(it) }
                .sorted()
            AndroidToolResult.success(
                JSONObject()
                    .put("name", prefs.first)
                    .put("keys", JSONArray(keys))
                    .put("count", keys.size),
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
            if (!options.isKeyAllowed(key)) {
                return@androidPreferencesTool AndroidToolResult.failure("Preference key is not allow-listed.", "preference_key_denied")
            }
            val prefs = preferences(context.application, args, options)
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
            if (!options.isKeyAllowed(key)) {
                return@androidPreferencesTool AndroidToolResult.failure("Preference key is not allow-listed.", "preference_key_denied")
            }
            val prefs = preferences(context.application, args, options)
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
            if (!options.isKeyAllowed(key)) {
                return@androidPreferencesTool AndroidToolResult.failure("Preference key is not allow-listed.", "preference_key_denied")
            }
            val prefs = preferences(context.application, args, options)
            prefs.second.edit().remove(key).apply()
            AndroidToolResult.success(JSONObject().put("name", prefs.first).put("key", key).put("removed", true))
        },
    )

    private fun preferences(
        application: Application,
        args: Map<String, String>,
        options: AndroidPreferencesToolsOptions,
    ): Pair<String, SharedPreferences> {
        val name = args["store"]?.trim()?.ifBlank { null }
            ?: args["name"]?.trim()?.ifBlank { null }
            ?: options.defaultStore
            ?: "${application.packageName}_preferences"
        require(options.isStoreAllowed(name)) { "Preferences store '$name' is not allow-listed." }
        return name to application.getSharedPreferences(name, Application.MODE_PRIVATE)
    }
}
