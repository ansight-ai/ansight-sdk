import AnsightCore
import AnsightToolsDatabase
import AnsightToolsFileSystem
import AnsightToolsPreferences
import AnsightToolsReflection
import AnsightToolsSecureStorage
import Foundation

public struct AnsightRemoteToolOptions: Sendable, Equatable {
    public static let `default` = AnsightRemoteToolOptions()

    public var database: AnsightDatabaseToolsOptions
    public var fileSystem: AnsightFileSystemToolsOptions
    public var preferences: AnsightPreferencesToolOptions
    public var reflection: AnsightReflectionToolsOptions
    public var secureStorage: AnsightSecureStorageToolsOptions
    public var artifactProviders: [any AnsightArtifactProvider]

    public init(
        database: AnsightDatabaseToolsOptions = .default,
        fileSystem: AnsightFileSystemToolsOptions = .default,
        preferences: AnsightPreferencesToolOptions = .default,
        reflection: AnsightReflectionToolsOptions = .default,
        secureStorage: AnsightSecureStorageToolsOptions = .default,
        artifactProviders: [any AnsightArtifactProvider] = []
    ) {
        self.database = database
        self.fileSystem = fileSystem
        self.preferences = preferences
        self.reflection = reflection
        self.secureStorage = secureStorage
        self.artifactProviders = artifactProviders
    }

    public static func == (lhs: AnsightRemoteToolOptions, rhs: AnsightRemoteToolOptions) -> Bool {
            lhs.database == rhs.database &&
            lhs.fileSystem == rhs.fileSystem &&
            lhs.preferences == rhs.preferences &&
            lhs.reflection == rhs.reflection &&
            lhs.secureStorage == rhs.secureStorage &&
            providerIds(lhs.artifactProviders) == providerIds(rhs.artifactProviders)
    }

    private static func providerIds(_ providers: [any AnsightArtifactProvider]) -> [String] {
        providers.compactMap { provider in
            provider.descriptor.id
        }
        .map { $0.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() }
        .filter { !$0.isEmpty }
        .sorted()
    }
}
