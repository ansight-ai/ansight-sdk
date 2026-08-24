package ai.ansight.tools.securestorage

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.ToolPolicy
import ai.ansight.runtime.androidSecureStorageTool
import ai.ansight.runtime.putNullable
import android.app.Application
import org.json.JSONObject

object AndroidSecureStorageTools {
    fun create(): List<AndroidTool> = listOf(
        androidSecureStorageTool(
            SecureStorageToolIds.GetValue,
            "Get Secure Storage Value",
            "Reads an explicitly allow-listed secure value.",
        ) { args, context ->
            val key = args["key"] ?: return@androidSecureStorageTool AndroidToolResult.failure(
                "Secure storage key is required.",
                "secure_key_required",
            )
            if (!context.options.secureStorage.isAllowed(key)) {
                return@androidSecureStorageTool AndroidToolResult.failure("Secure storage key is not allow-listed.", "secure_key_denied")
            }
            val prefs = context.application.getSharedPreferences(context.options.secureStorage.preferencesName, Application.MODE_PRIVATE)
            AndroidToolResult.success(
                JSONObject()
                    .put("key", key)
                    .putNullable("value", prefs.getString(key, null))
                    .put("exists", prefs.contains(key)),
            )
        },
        androidSecureStorageTool(
            SecureStorageToolIds.SetValue,
            "Set Secure Storage Value",
            "Writes an explicitly allow-listed secure value.",
            ToolPolicy.Critical,
        ) { args, context ->
            val key = args["key"] ?: return@androidSecureStorageTool AndroidToolResult.failure(
                "Secure storage key is required.",
                "secure_key_required",
            )
            if (!context.options.secureStorage.isAllowed(key)) {
                return@androidSecureStorageTool AndroidToolResult.failure("Secure storage key is not allow-listed.", "secure_key_denied")
            }
            val prefs = context.application.getSharedPreferences(context.options.secureStorage.preferencesName, Application.MODE_PRIVATE)
            prefs.edit().putString(key, args["value"] ?: "").apply()
            AndroidToolResult.success(JSONObject().put("key", key).put("written", true))
        },
        androidSecureStorageTool(
            SecureStorageToolIds.RemoveKey,
            "Remove Secure Storage Key",
            "Removes an explicitly allow-listed secure value.",
            ToolPolicy.Critical,
        ) { args, context ->
            val key = args["key"] ?: return@androidSecureStorageTool AndroidToolResult.failure(
                "Secure storage key is required.",
                "secure_key_required",
            )
            if (!context.options.secureStorage.isAllowed(key)) {
                return@androidSecureStorageTool AndroidToolResult.failure("Secure storage key is not allow-listed.", "secure_key_denied")
            }
            val prefs = context.application.getSharedPreferences(context.options.secureStorage.preferencesName, Application.MODE_PRIVATE)
            prefs.edit().remove(key).apply()
            AndroidToolResult.success(JSONObject().put("key", key).put("removed", true))
        },
    )
}
