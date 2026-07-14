package ai.ansight

import ai.ansight.runtime.AnsightOptionsBuilder
import ai.ansight.tools.database.DatabaseToolIds
import ai.ansight.tools.database.withDatabaseTools
import ai.ansight.tools.filedescriptordiagnostics.FileDescriptorDiagnosticsToolIds
import ai.ansight.tools.filedescriptordiagnostics.withFileDescriptorDiagnosticsTools
import ai.ansight.tools.filesystem.FileSystemToolIds
import ai.ansight.tools.filesystem.withFileSystemTools
import ai.ansight.tools.preferences.PreferencesToolIds
import ai.ansight.tools.preferences.withPreferencesTools
import ai.ansight.tools.reflection.ReflectionToolIds
import ai.ansight.tools.reflection.withReflectionTools
import ai.ansight.tools.securestorage.SecureStorageToolIds
import ai.ansight.tools.securestorage.withSecureStorageTools
import ai.ansight.tools.visualtree.VisualTreeToolIds
import ai.ansight.tools.visualtree.withVisualTreeTools

fun AnsightOptionsBuilder.withAnsightSdk(): AnsightOptionsBuilder {
    return withAnsightSdk {}
}

fun AnsightOptionsBuilder.withAnsightSdk(
    configure: AnsightOptionsBuilder.() -> Unit,
): AnsightOptionsBuilder {
    withAnsightDefaults()
    withAllToolAccess()
    configure()
    return withAnsightRemoteTools()
}

fun AnsightOptionsBuilder.withAnsightDefaults(): AnsightOptionsBuilder {
    return withFramesPerSecond()
        .withSampleFrequencyMilliseconds(400)
        .withRetentionPeriodSeconds(120)
        .withSessionJpegCapture(
            intervalMilliseconds = 2_000,
            quality = 60,
            maxWidth = 480,
        )
        .withTouchCapture()
        .withHostAutoProbe()
}

fun AnsightOptionsBuilder.withAnsightRemoteTools(): AnsightOptionsBuilder {
    if (!containsAnyTool(visualTreeSuiteToolIds)) {
        withVisualTreeTools()
    }
    if (!containsAnyTool(databaseSuiteToolIds)) {
        withDatabaseTools()
    }
    if (!containsAnyTool(fileSystemSuiteToolIds)) {
        withFileSystemTools()
    }
    if (!containsAnyTool(fileDescriptorDiagnosticsSuiteToolIds)) {
        withFileDescriptorDiagnosticsTools()
    }
    if (!containsAnyTool(preferencesSuiteToolIds)) {
        withPreferencesTools()
    }
    if (!containsAnyTool(reflectionSuiteToolIds)) {
        withReflectionTools()
    }
    if (!containsAnyTool(secureStorageSuiteToolIds)) {
        withSecureStorageTools()
    }

    return this
}

private fun AnsightOptionsBuilder.containsAnyTool(toolIds: Iterable<String>): Boolean {
    return toolIds.any { containsTool(it) }
}

private val visualTreeSuiteToolIds = listOf(
    VisualTreeToolIds.GetVisualTree,
    VisualTreeToolIds.GetScreenshot,
    VisualTreeToolIds.InspectNode,
    VisualTreeToolIds.ShowOverlay,
    VisualTreeToolIds.GetOverlay,
    VisualTreeToolIds.QueryOverlays,
    VisualTreeToolIds.UpdateOverlay,
    VisualTreeToolIds.RemoveOverlay,
    VisualTreeToolIds.ClearOverlays,
)

private val databaseSuiteToolIds = listOf(
    DatabaseToolIds.ListDatabases,
    DatabaseToolIds.DescribeSchema,
    DatabaseToolIds.Query,
)

private val fileSystemSuiteToolIds = listOf(
    FileSystemToolIds.ListDirectory,
    FileSystemToolIds.ReadFile,
    FileSystemToolIds.GetFileChecksum,
    FileSystemToolIds.DownloadFile,
    FileSystemToolIds.BeginBinaryDownload,
    FileSystemToolIds.PushFile,
    FileSystemToolIds.CopyFile,
    FileSystemToolIds.MoveFile,
    FileSystemToolIds.DeleteFile,
)

private val fileDescriptorDiagnosticsSuiteToolIds = listOf(
    FileDescriptorDiagnosticsToolIds.ListOpen,
    FileDescriptorDiagnosticsToolIds.CountOpen,
    FileDescriptorDiagnosticsToolIds.Inspect,
    FileDescriptorDiagnosticsToolIds.GetUsage,
)

private val preferencesSuiteToolIds = listOf(
    PreferencesToolIds.ListKeys,
    PreferencesToolIds.GetValue,
    PreferencesToolIds.SetValue,
    PreferencesToolIds.RemoveKey,
)

private val reflectionSuiteToolIds = listOf(
    ReflectionToolIds.ListRoots,
    ReflectionToolIds.InspectObject,
    ReflectionToolIds.DescribeType,
    ReflectionToolIds.SetMemberValue,
    ReflectionToolIds.InvokeMethod,
)

private val secureStorageSuiteToolIds = listOf(
    SecureStorageToolIds.GetValue,
    SecureStorageToolIds.SetValue,
    SecureStorageToolIds.RemoveKey,
)
