package ai.ansight.tools.clipboard

import ai.ansight.runtime.*
import android.app.Application
import org.junit.Assert.*
import org.junit.Test
import org.json.JSONObject

class ClipboardToolsTest {
    private val context = AndroidToolExecutionContext(Application(), null, "clipboard-test", "request", AnsightOptions())

    @Test fun roundTripPreservesTextAndClear() {
        val backend = FakeClipboard()
        val tools = AndroidClipboardTools.create(backend).associateBy { it.definition.id }
        for (text in listOf("", "123", "true", "null", "  hello\n世界 🧗\t")) {
            assertTrue(tools.getValue(ClipboardToolIds.SetText).execute(mapOf("text" to text), context).success)
            val result = tools.getValue(ClipboardToolIds.GetText).invoke(emptyMap())
            assertEquals(text, result.payload!!.getString("text"))
            assertTrue(result.payload!!.getBoolean("hasText"))
        }
        assertTrue(tools.getValue(ClipboardToolIds.Clear).invoke(emptyMap()).success)
        val result = tools.getValue(ClipboardToolIds.GetText).invoke(emptyMap())
        assertTrue(result.payload!!.isNull("text"))
        assertFalse(result.payload!!.getBoolean("hasText"))
        val schema = tools.getValue(ClipboardToolIds.GetText).definition.resultSchema
        assertTrue(ToolSchemaValidator.validate(schema, result.payload).isEmpty())
        assertFalse(ToolSchemaValidator.validate(schema, JSONObject().put("hasText", false)).isEmpty())
    }
    @Test fun validationAndAccessFailuresDoNotSucceed() {
        val backend = FakeClipboard()
        val tools = AndroidClipboardTools.create(backend).associateBy { it.definition.id }
        val write = tools.getValue(ClipboardToolIds.SetText)
        assertEquals("clipboard_invalid_arguments", write.invoke(emptyMap()).errorCode)
        assertEquals("clipboard_text_too_large", write.invoke(mapOf("text" to "界".repeat(21846))).errorCode)
        assertNull(backend.contents)
        backend.unavailable = true
        assertEquals("clipboard_not_focused", tools.getValue(ClipboardToolIds.GetText).invoke(emptyMap()).errorCode)
        assertEquals(ToolPolicy.Write, write.definition.policy)
        assertEquals(ToolPolicy.Write, tools.getValue(ClipboardToolIds.Clear).definition.policy)
        assertEquals(ToolPolicy.Read, tools.getValue(ClipboardToolIds.HasText).definition.policy)
    }
    private fun AndroidTool.invoke(args: Map<String, String>): AndroidToolResult =
        (this as JsonAndroidTool).executeJson(JSONObject(args), context)

    private class FakeClipboard : ClipboardBackend {
        var contents: String? = null
        var unavailable = false
        override fun getText(): String? {
            if (unavailable) throw ClipboardFailure("clipboard_not_focused", "Focus the app.")
            return contents
        }
        override fun hasText() = getText() != null
        override fun setText(text: String) { contents = text }
        override fun clear() { contents = null }
    }
}
