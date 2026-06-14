import Foundation

internal struct AnsightFileSystemResolvedPath: Sendable, Equatable {
    let rootAlias: String
    let rootPath: String
    let fullPath: String
}
