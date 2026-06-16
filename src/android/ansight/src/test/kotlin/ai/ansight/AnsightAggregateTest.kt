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
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

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
