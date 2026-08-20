package ai.ansight

import ai.ansight.runtime.AndroidTool
import ai.ansight.tools.database.AndroidDatabaseTools
import ai.ansight.tools.filedescriptordiagnostics.AndroidFileDescriptorDiagnosticsTools
import ai.ansight.tools.filesystem.AndroidFileSystemTools
import ai.ansight.tools.jnireferencediagnostics.AndroidJniReferenceDiagnosticsTools
import ai.ansight.tools.preferences.AndroidPreferencesTools
import ai.ansight.tools.reflection.AndroidReflectionTools
import ai.ansight.tools.securestorage.AndroidSecureStorageTools
import ai.ansight.tools.visualtree.AndroidVisualTreeTools

object AnsightStandardTools {
    fun create(): List<AndroidTool> =
        AndroidVisualTreeTools.create() +
            AndroidFileDescriptorDiagnosticsTools.create() +
            AndroidJniReferenceDiagnosticsTools.create() +
            AndroidFileSystemTools.create() +
            AndroidPreferencesTools.create() +
            AndroidSecureStorageTools.create() +
            AndroidDatabaseTools.create() +
            AndroidReflectionTools.create()
}
