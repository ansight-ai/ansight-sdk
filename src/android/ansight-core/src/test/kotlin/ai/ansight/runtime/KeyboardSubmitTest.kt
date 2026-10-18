package ai.ansight.runtime

import android.app.Activity
import android.view.inputmethod.EditorInfo
import android.widget.EditText
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(manifest = Config.NONE, sdk = [28])
class KeyboardSubmitTest {
    @Test
    fun capturedInputValueNeverFallsBackToPlaceholder() {
        val controller = Robolectric.buildActivity(Activity::class.java).setup().visible()
        val activity = controller.get()
        val editor = EditText(activity).apply { hint = "Search products…" }
        activity.setContentView(editor)
        AndroidUiEvidence.onActivityResumed(activity)
        fun nodes(node: org.json.JSONObject): List<org.json.JSONObject> {
            val children = node.optJSONArray("children") ?: org.json.JSONArray()
            return listOf(node) + (0 until children.length()).flatMap { nodes(children.getJSONObject(it)) }
        }
        try {
            for (value in listOf("", "  query\n", "Search products…")) {
                editor.setText(value)
                val tree = AndroidUiEvidence.visualTree()
                val input = nodes(tree.getJSONObject("root")).first { it.optString("role") == "textbox" }
                assertEquals(value, input.getJSONObject("visual").getString("value"))
                assertEquals("Search products…", input.getJSONObject("visual").getString("text"))
            }
            editor.transformationMethod = android.text.method.PasswordTransformationMethod.getInstance()
            val input = nodes(AndroidUiEvidence.visualTree().getJSONObject("root")).first { it.optString("role") == "textbox" }
            assertFalse(input.getJSONObject("visual").has("value"))
        } finally {
            AndroidUiEvidence.onActivityDestroyed(activity)
            controller.pause().stop().destroy()
        }
    }

    @Test
    fun configuredSearchActionInvokesListenerExactlyOnceWithoutChangingText() {
        val controller = Robolectric.buildActivity(Activity::class.java).setup().visible()
        val activity = controller.get()
        val editor = EditText(activity).apply {
            setSingleLine(true)
            imeOptions = EditorInfo.IME_ACTION_SEARCH
            setText("query")
        }
        var actions = 0
        editor.setOnEditorActionListener { _, action, _ ->
            assertEquals(EditorInfo.IME_ACTION_SEARCH, action)
            actions++
            true
        }
        activity.setContentView(editor)
        editor.requestFocus()
        controller.windowFocusChanged(true)
        AndroidUiEvidence.onActivityResumed(activity)
        try {
            editor.requestFocus()
            org.robolectric.Shadows.shadowOf(activity).setCurrentFocus(editor)
            org.robolectric.Shadows.shadowOf(android.os.Looper.getMainLooper()).idle()
            assertTrue("Window is not focused", activity.hasWindowFocus())
            assertSame("Editor is not focused", editor, activity.currentFocus)
            val result = AndroidUiEvidence.submitKeyboard()
            assertTrue(result.message ?: "Submit failed", result.success)
            assertEquals(1, actions)
            assertEquals("query", editor.text.toString())
        } finally {
            AndroidUiEvidence.onActivityDestroyed(activity)
            controller.pause().stop().destroy()
        }
    }

    @Test
    fun noFocusedEditorFailsWithoutDispatchingAnAction() {
        val controller = Robolectric.buildActivity(Activity::class.java).setup().visible()
        val activity = controller.get()
        controller.windowFocusChanged(true)
        AndroidUiEvidence.onActivityResumed(activity)
        try {
            assertFalse(AndroidUiEvidence.submitKeyboard().success)
        } finally {
            AndroidUiEvidence.onActivityDestroyed(activity)
            controller.pause().stop().destroy()
        }
    }
}
