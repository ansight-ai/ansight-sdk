import Foundation

public final class AnsightFileSystemToolsOptionsBuilder {
    private var rootsByAlias: [String: AnsightFileSystemRoot] = [:]

    public init() {}

    @discardableResult
    public func addRoot(alias: String, path: String) -> AnsightFileSystemToolsOptionsBuilder {
        let root = AnsightFileSystemRoot(alias: alias, path: path)
        if !root.alias.isEmpty && !root.path.isEmpty {
            rootsByAlias[root.alias.lowercased()] = root
        }

        return self
    }

    public func build() -> AnsightFileSystemToolsOptions {
        AnsightFileSystemToolsOptions(
            additionalRoots: rootsByAlias.values.sorted { $0.alias.localizedCaseInsensitiveCompare($1.alias) == .orderedAscending }
        )
    }
}
