package ai.ansight

import ai.ansight.runtime.AndroidStandardTools
import ai.ansight.tools.database.DatabaseToolIds
import ai.ansight.tools.filesystem.FileSystemToolIds
import ai.ansight.tools.preferences.PreferencesToolIds
import ai.ansight.tools.reflection.ReflectionToolIds
import ai.ansight.tools.securestorage.SecureStorageToolIds
import ai.ansight.tools.visualtree.AndroidVisualTreeProvider
import ai.ansight.tools.visualtree.AndroidVisualTreeProviderRegistry
import ai.ansight.tools.visualtree.VisualTreeToolIds
import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.runtime.AnsightOptions
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ai.ansight.tools.filesystem.withFileSystemTools

class AnsightAggregateTest {
    @Test
    fun standardToolsIncludeNativeSuitesFromConstants() {
        val ids = AndroidStandardTools.create().map { it.definition.id }.toSet()

        val expected = listOf(
            VisualTreeToolIds.GetVisualTree,
            VisualTreeToolIds.GetScreenshot,
            VisualTreeToolIds.InspectNode,
            VisualTreeToolIds.ShowOverlay,
            VisualTreeToolIds.GetOverlay,
            VisualTreeToolIds.QueryOverlays,
            VisualTreeToolIds.UpdateOverlay,
            VisualTreeToolIds.RemoveOverlay,
            VisualTreeToolIds.ClearOverlays,
            FileSystemToolIds.ListDirectory,
            FileSystemToolIds.ReadFile,
            FileSystemToolIds.GetFileChecksum,
            FileSystemToolIds.DownloadFile,
            FileSystemToolIds.BeginBinaryDownload,
            FileSystemToolIds.PushFile,
            FileSystemToolIds.CopyFile,
            FileSystemToolIds.MoveFile,
            FileSystemToolIds.DeleteFile,
            PreferencesToolIds.ListKeys,
            PreferencesToolIds.GetValue,
            PreferencesToolIds.SetValue,
            PreferencesToolIds.RemoveKey,
            SecureStorageToolIds.GetValue,
            SecureStorageToolIds.SetValue,
            SecureStorageToolIds.RemoveKey,
            DatabaseToolIds.ListDatabases,
            DatabaseToolIds.DescribeSchema,
            DatabaseToolIds.Query,
            ReflectionToolIds.ListRoots,
            ReflectionToolIds.InspectObject,
            ReflectionToolIds.DescribeType,
            ReflectionToolIds.SetMemberValue,
            ReflectionToolIds.InvokeMethod,
        )

        assertEquals(expected.size, ids.size)
        expected.forEach { id ->
            assertTrue("Missing tool id $id", ids.contains(id))
        }
    }

    @Test
    fun developerOptionsWireAllStandardTools() {
        assertEquals(33, Ansight.developerOptions().initialTools.size)
    }

    @Test
    fun optionsBuilderOverloadKeepsStandardToolsAndCustomizesRuntimeOptions() {
        val options = Ansight.options {
            withReadOnlyToolAccess()
            withSessionJpegCapture(intervalMilliseconds = 1_500, quality = 65, maxWidth = 600)
        }

        assertEquals(33, options.initialTools.size)
        assertEquals(AnsightToolGuard.ReadOnly, options.toolGuard)
        assertEquals(1_500, options.sessionJpegCapture?.intervalMilliseconds)
        assertEquals(65, options.sessionJpegCapture?.quality)
        assertEquals(600, options.sessionJpegCapture?.maxWidth)
        assertEquals(true, options.sessionJpegCapture?.captureGpuBackedSurfaces)
    }

    @Test
    fun withAnsightSdkPreservesExplicitSuiteRegistrationAndAddsRemainingTools() {
        val options = AnsightOptions.createBuilder()
            .withAnsightSdk {
                withFileSystemTools {
                    addRoot("exports", "/tmp/ansight-exports")
                }
            }
            .build()
        val toolIds = options.initialTools.map { it.definition.id }

        assertEquals(33, toolIds.size)
        assertEquals(33, toolIds.toSet().size)
        assertEquals(AnsightToolGuard.FullAccess, options.toolGuard)
        assertTrue(toolIds.contains(FileSystemToolIds.ListDirectory))
        assertTrue(toolIds.contains(VisualTreeToolIds.GetVisualTree))
    }

    @Test
    fun visualTreeProviderRegistryTracksCustomSources() {
        val provider = object : AndroidVisualTreeProvider {
            override val source = "unit-test"
            override val displayName = "Unit Test"

            override fun getVisualTree(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult {
                return AndroidToolResult.success(
                    JSONObject()
                        .put("platform", "test")
                        .put("source", source)
                        .put("adapter", "unit.test"),
                )
            }

            override fun inspectNode(arguments: Map<String, String>, context: AndroidToolExecutionContext): AndroidToolResult {
                return AndroidToolResult.success(
                    JSONObject()
                        .put("platform", "test")
                        .put("source", source)
                        .put("node", JSONObject().put("id", arguments["nodeId"] ?: "root")),
                )
            }
        }

        AndroidVisualTreeProviderRegistry.register(provider)

        assertEquals(provider, AndroidVisualTreeProviderRegistry.provider("Unit-Test"))
        assertTrue(AndroidVisualTreeProviderRegistry.registeredSources().contains("unit-test"))
    }
}
