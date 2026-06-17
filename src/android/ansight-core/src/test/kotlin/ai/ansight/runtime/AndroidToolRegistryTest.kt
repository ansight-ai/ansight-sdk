package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AndroidToolRegistryTest {
    @Test
    fun duplicateRegistrationIsRejectedByDefault() {
        val registry = AndroidToolRegistry()
        val first = tool("app.test.tool", "first")
        val second = tool("app.test.tool", "second")

        registry.register(first)
        val failure = runCatching { registry.register(second) }.exceptionOrNull()

        assertTrue(failure is IllegalArgumentException)
        assertEquals("first", registry.get("app.test.tool")!!.definition.name)
    }

    @Test
    fun duplicateRegistrationCanReplaceExistingTool() {
        val registry = AndroidToolRegistry()
        val first = tool("app.test.tool", "first")
        val second = tool("app.test.tool", "second")

        registry.register(first)
        registry.register(second, replaceExisting = true)

        assertEquals(1, registry.size)
        assertTrue(registry.contains("app.test.tool"))
        assertFalse(registry.contains("app.test.missing"))
        assertEquals("second", registry.get("app.test.tool")!!.definition.name)
    }

    private fun tool(id: String, name: String): AndroidTool =
        FunctionAndroidTool(
            ToolDefinition(
                id = id,
                name = name,
                description = "Test tool",
                category = "test",
                scope = ToolScope.Read,
                keywords = "test",
            ),
        ) { _, _ -> AndroidToolResult.success() }
}
