package ai.ansight.tools.jnireferencediagnostics

import ai.ansight.runtime.ToolPolicy
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class JniReferenceDiagnosticsToolsTest {
    @Test
    fun toolSuiteExposesBoundedReadOnlyGraphContract() {
        val tool = AndroidJniReferenceDiagnosticsTools.create().single()

        assertEquals(JniReferenceDiagnosticsToolIds.CaptureGraph, tool.definition.id)
        assertEquals("jni_references", tool.definition.category)
        assertEquals(ToolPolicy.Read, tool.definition.policy)
        assertEquals(
            setOf("maxNodes", "maxEdges", "maxDepth"),
            tool.definition.argumentsSchema.properties.keys,
        )
        assertTrue(tool.definition.resultSchema.required.contains("nodes"))
        assertTrue(tool.definition.resultSchema.required.contains("edges"))
        assertTrue(tool.definition.resultSchema.required.contains("truncated"))
    }

    @Test
    fun optionsClampGraphBounds() {
        val options = AndroidJniReferenceDiagnosticsOptions(
            maximumGraphNodes = 100_000,
            maximumGraphEdges = 0,
            maximumGraphDepth = 100,
        )

        assertEquals(8_192, options.validatedMaximumGraphNodes)
        assertEquals(1, options.validatedMaximumGraphEdges)
        assertEquals(16, options.validatedMaximumGraphDepth)
    }
}
