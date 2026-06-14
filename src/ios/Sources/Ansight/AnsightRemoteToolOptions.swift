import AnsightToolsDatabase
import AnsightToolsFileSystem
import AnsightToolsPreferences
import AnsightToolsSecureStorage
import Foundation

public struct AnsightRemoteToolOptions: Sendable, Equatable {
    public static let `default` = AnsightRemoteToolOptions()

    public var database: AnsightDatabaseToolsOptions
    public var fileSystem: AnsightFileSystemToolsOptions
    public var preferences: AnsightPreferencesToolOptions
    public var secureStorage: AnsightSecureStorageToolsOptions

    public init(
        database: AnsightDatabaseToolsOptions = .default,
        fileSystem: AnsightFileSystemToolsOptions = .default,
        preferences: AnsightPreferencesToolOptions = .default,
        secureStorage: AnsightSecureStorageToolsOptions = .default
    ) {
        self.database = database
        self.fileSystem = fileSystem
        self.preferences = preferences
        self.secureStorage = secureStorage
    }
}
