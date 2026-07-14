import AnsightCore
import AnsightToolsDatabase
import AnsightToolsFileDescriptorDiagnostics
import AnsightToolsFileSystem
import AnsightToolsPreferences
import AnsightToolsReflection
import AnsightToolsSecureStorage
import AnsightToolsVisualTree
import Foundation

public enum AnsightRemoteTools {
    public static func tools(
        options: AnsightRemoteToolOptions = .default,
        runtime: AnsightRuntime = .shared
    ) -> [any AnsightTool] {
        let visualTreeTools: [any AnsightTool]
        if options.visualTree {
            visualTreeTools = AnsightVisualTreeTools.tools(runtime: runtime)
        } else {
            visualTreeTools = []
        }
        let artifactTools = options.artifactProviders.isEmpty
            ? []
            : AnsightArtifactTools.tools(providers: options.artifactProviders, runtime: runtime)

        return visualTreeTools
            + AnsightDatabaseTools.tools(options: options.database)
            + AnsightFileDescriptorDiagnosticsTools.tools(options: options.fileDescriptorDiagnostics)
            + AnsightFileSystemTools.tools(options: options.fileSystem)
            + AnsightPreferencesTools.tools(options: options.preferences)
            + AnsightReflectionTools.tools(options: options.reflection, runtime: runtime)
            + AnsightSecureStorageTools.tools(options: options.secureStorage)
            + artifactTools
    }
}
