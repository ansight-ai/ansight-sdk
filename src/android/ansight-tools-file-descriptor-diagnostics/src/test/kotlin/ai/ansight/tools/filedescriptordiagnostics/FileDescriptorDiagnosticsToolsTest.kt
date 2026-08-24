package ai.ansight.tools.filedescriptordiagnostics

import ai.ansight.runtime.ToolPolicy
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FileDescriptorDiagnosticsToolsTest {
    @Test
    fun toolSuiteExposesExpectedReadOnlyContract() {
        val tools = AndroidFileDescriptorDiagnosticsTools.create()
        val ids = tools.map { it.definition.id }

        assertEquals(
            listOf(
                FileDescriptorDiagnosticsToolIds.ListOpen,
                FileDescriptorDiagnosticsToolIds.CountOpen,
                FileDescriptorDiagnosticsToolIds.Inspect,
                FileDescriptorDiagnosticsToolIds.GetUsage,
            ),
            ids,
        )
        assertTrue(tools.all { it.definition.policy == ToolPolicy.Read })
        assertTrue(tools.all { it.definition.category == "file_descriptors" })
        assertTrue(tools.all { it.definition.argumentsSchema.type == "object" })
        assertTrue(tools.all { it.definition.resultSchema.type == "object" })
        val countSchema = tools
            .single { it.definition.id == FileDescriptorDiagnosticsToolIds.CountOpen }
            .definition
            .resultSchema
        assertEquals(setOf("count"), countSchema.properties.keys)
        assertEquals(listOf("count"), countSchema.required)
    }

    @Test
    fun optionsClampReturnedDescriptorLimit() {
        assertEquals(1, AndroidFileDescriptorDiagnosticsOptions(maximumReturnedDescriptors = 0).validatedMaximumReturnedDescriptors)
        assertEquals(8_192, AndroidFileDescriptorDiagnosticsOptions(maximumReturnedDescriptors = 10_000).validatedMaximumReturnedDescriptors)
        assertFalse(AndroidFileDescriptorDiagnosticsOptions(includeTargets = false).includeTargets)
    }
}
