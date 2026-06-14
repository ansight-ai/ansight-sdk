import AnsightKit
import AnsightToolsDatabase
import AnsightToolsFileSystem
import AnsightToolsPreferences
import AnsightToolsSecureStorage
import AnsightToolsVisualTree
import Foundation

public enum AnsightRemoteTools {
    public static func tools(
        options: AnsightRemoteToolOptions = .default,
        runtime: AnsightRuntime = .shared
    ) -> [any AnsightTool] {
        AnsightVisualTreeTools.tools(runtime: runtime)
            + AnsightDatabaseTools.tools(options: options.database)
            + AnsightFileSystemTools.tools(options: options.fileSystem)
            + AnsightPreferencesTools.tools(options: options.preferences)
            + AnsightSecureStorageTools.tools(options: options.secureStorage)
    }
}
