package ai.ansight.tools.clipboard

import ai.ansight.runtime.*
import android.app.Application
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.os.Build
import android.os.Handler
import android.os.Looper
import org.json.JSONObject
import java.util.concurrent.FutureTask

object ClipboardToolIds {
    const val GetText = "clipboard.get_text"
    const val HasText = "clipboard.has_text"
    const val SetText = "clipboard.set_text"
    const val Clear = "clipboard.clear"
}

internal interface ClipboardBackend {
    fun getText(): String?
    fun hasText(): Boolean
    fun setText(text: String)
    fun clear()
}

internal class ClipboardFailure(val code: String, message: String) : Exception(message)

object AndroidClipboardTools {
    @JvmStatic
    fun create(): List<AndroidTool> = create(null)

    internal fun create(backend: ClipboardBackend?): List<AndroidTool> =
        listOf(ClipboardToolIds.GetText, ClipboardToolIds.HasText, ClipboardToolIds.SetText, ClipboardToolIds.Clear).map { id ->
            val fields = when (id) {
                ClipboardToolIds.GetText -> mapOf("text" to ToolSchema.string(nullable = true), "hasText" to ToolSchema.bool())
                ClipboardToolIds.HasText -> mapOf("hasText" to ToolSchema.bool())
                ClipboardToolIds.SetText -> mapOf("updated" to ToolSchema.bool())
                else -> mapOf("cleared" to ToolSchema.bool())
            }
            ClipboardAndroidTool(
                ToolDefinition(
                    id = id, name = id, category = "clipboard",
                    description = "Access the system clipboard of the focused app. Text is limited to 65536 UTF-8 bytes.",
                    policy = if (id == ClipboardToolIds.SetText || id == ClipboardToolIds.Clear) ToolPolicy.Write else ToolPolicy.Read,
                    keywords = "clipboard copy paste text",
                    argumentsSchema = if (id == ClipboardToolIds.SetText) ToolSchema.obj(
                        properties = mapOf("text" to ToolSchema.string("Exact plain text; empty text is allowed.")), required = listOf("text"),
                    ) else ToolSchema.obj(),
                    resultSchema = ToolSchema.obj(properties = fields, required = fields.keys.toList()),
                ),
            ) { jsonArgs, context ->
                try {
                    val args = jsonArgs.keys().asSequence().associateWith { key ->
                        jsonArgs.get(key) as? String ?: throw ClipboardFailure("clipboard_invalid_arguments", "Clipboard arguments must be strings.")
                    }
                    if (args.keys.any { id != ClipboardToolIds.SetText || it != "text" }) {
                        throw ClipboardFailure("clipboard_invalid_arguments", "Unexpected clipboard argument.")
                    }
                    if (id == ClipboardToolIds.SetText) validateText(args["text"]
                        ?: throw ClipboardFailure("clipboard_invalid_arguments", "The text argument is required (an empty string is allowed)."))
                    val result = if (backend != null) perform(id, args, backend) else onMain {
                        perform(id, args, NativeClipboardBackend(context.application))
                    }
                    AndroidToolResult.success(result)
                } catch (error: Exception) {
                    val cause = (error as? java.util.concurrent.ExecutionException)?.cause ?: error
                    if (cause is ClipboardFailure) AndroidToolResult.failure(cause.message ?: "Clipboard unavailable.", cause.code)
                    else AndroidToolResult.failure("The clipboard operation failed.", "clipboard_operation_failed")
                }
            }
        }

    private fun perform(id: String, args: Map<String, String>, backend: ClipboardBackend): JSONObject = when (id) {
        ClipboardToolIds.GetText -> {
            val text = backend.getText()
            if (text != null) validateText(text)
            JSONObject().put("text", text ?: JSONObject.NULL).put("hasText", text != null)
        }
        ClipboardToolIds.HasText -> JSONObject().put("hasText", backend.hasText())
        ClipboardToolIds.SetText -> { backend.setText(args.getValue("text")); JSONObject().put("updated", true) }
        else -> { backend.clear(); JSONObject().put("cleared", true) }
    }

    private fun validateText(text: String) {
        if (text.toByteArray(Charsets.UTF_8).size > 65_536)
            throw ClipboardFailure("clipboard_text_too_large", "Clipboard text exceeds 65536 UTF-8 bytes.")
    }
    private fun <T> onMain(block: () -> T): T {
        if (Looper.myLooper() == Looper.getMainLooper()) return block()
        val task = FutureTask { block() }
        if (!Handler(Looper.getMainLooper()).post(task))
            throw ClipboardFailure("clipboard_unavailable", "The app main thread is unavailable.")
        return task.get()
    }
}

private class ClipboardAndroidTool(
    override val definition: ToolDefinition,
    private val handler: (JSONObject, AndroidToolExecutionContext) -> AndroidToolResult,
) : JsonAndroidTool {
    override fun executeJson(arguments: JSONObject, context: AndroidToolExecutionContext) = handler(arguments, context)
    // Clipboard text is opaque: do not reinterpret "123", "true" or "null" as JSON values.
    override fun execute(arguments: Map<String, String>, context: AndroidToolExecutionContext) =
        handler(JSONObject(arguments), context)
}

private class NativeClipboardBackend(private val application: Application) : ClipboardBackend {
    private fun manager(): ClipboardManager {
        val activity = AndroidUiEvidence.bindCurrentActivity(application)
        if (activity?.window?.decorView?.hasWindowFocus() != true)
            throw ClipboardFailure("clipboard_not_focused", "Focus the app window before accessing its clipboard.")
        return activity.getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager
            ?: throw ClipboardFailure("clipboard_unavailable", "The system clipboard service is unavailable.")
    }
    override fun getText(): String? {
        val manager = manager()
        val clip = manager.primaryClip
        if (clip == null) {
            if (manager.hasPrimaryClip()) throw ClipboardFailure("clipboard_unavailable", "Clipboard contents could not be read.")
            return null
        }
        return if (clip.itemCount > 0) clip.getItemAt(0).text?.toString() else null
    }
    override fun hasText(): Boolean {
        val description = manager().primaryClipDescription ?: return false
        return description.hasMimeType("text/plain") || description.hasMimeType("text/html")
    }
    override fun setText(text: String) { manager().setPrimaryClip(ClipData.newPlainText("Ansight", text)) }
    override fun clear() {
        val manager = manager()
        if (Build.VERSION.SDK_INT < 28) throw ClipboardFailure("clipboard_clear_unsupported", "Clearing the clipboard requires Android API 28 or later.")
        manager.clearPrimaryClip()
    }
}

fun AnsightOptionsBuilder.withClipboardTools(): AnsightOptionsBuilder = addTools(AndroidClipboardTools.create())
