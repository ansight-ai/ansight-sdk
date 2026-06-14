import Foundation

internal struct AnsightDatabaseResolvedPath: Sendable, Equatable {
    let rootAlias: String
    let rootPath: String
    let fullPath: String

    var relativePath: String {
        AnsightDatabaseSandbox.relativePath(rootPath: rootPath, fullPath: fullPath)
    }
}
