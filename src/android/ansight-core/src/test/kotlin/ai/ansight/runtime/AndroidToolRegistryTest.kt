package ai.ansight.runtime

import android.app.Application
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

    @Test
    fun registeredToolReportsRuntimePrecondition() {
        val unavailableTool = FunctionAndroidTool(
            definition = ToolDefinition(
                id = "mapwork.open",
                name = "Open Map Work",
                description = "Opens the active map work screen.",
                category = "mapwork",
                policy = ToolPolicy.Read,
                keywords = "map work",
            ),
            availabilityHandler = {
                ToolAvailability.unavailable(
                    reasonCode = "screen_not_registered",
                    reason = "No active MapWorkScreen is registered.",
                    requiredState = "MapWorkScreen registered",
                    remediation = "Navigate to the map screen and retry.",
                )
            },
        ) { _, _ -> AndroidToolResult.success() }
        val registry = AndroidToolRegistry(listOf(unavailableTool))
        val availability = registry.get("mapwork.open")!!.availability(
            AndroidToolExecutionContext(
                application = Application(),
                transport = null,
                sessionId = "session_1",
                requestId = "query_1",
                options = AnsightOptions(),
            ),
        )

        assertFalse(availability.available)
        assertEquals("screen_not_registered", availability.reasonCode)
        assertEquals("MapWorkScreen registered", availability.requiredState)
        assertTrue(availability.retryable)
    }

    private fun tool(id: String, name: String): AndroidTool =
        FunctionAndroidTool(
            ToolDefinition(
                id = id,
                name = name,
                description = "Test tool",
                category = "test",
                policy = ToolPolicy.Read,
                keywords = "test",
            ),
        ) { _, _ -> AndroidToolResult.success() }
}
