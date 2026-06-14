import Foundation

public final class AnsightDatabaseToolsOptionsBuilder {
    private var rootsByAlias: [String: AnsightDatabaseRoot] = [:]
    private var includePlatformRootsValue = true

    public init() {}

    @discardableResult
    public func addRoot(alias: String, path: String) -> AnsightDatabaseToolsOptionsBuilder {
        let root = AnsightDatabaseRoot(alias: alias, path: path)
        if !root.alias.isEmpty && !root.path.isEmpty {
            rootsByAlias[root.alias.lowercased()] = root
        }

        return self
    }

    @discardableResult
    public func includePlatformRoots(_ includePlatformRoots: Bool) -> AnsightDatabaseToolsOptionsBuilder {
        includePlatformRootsValue = includePlatformRoots
        return self
    }

    public func build() -> AnsightDatabaseToolsOptions {
        AnsightDatabaseToolsOptions(
            additionalRoots: rootsByAlias.values.sorted { $0.alias.localizedCaseInsensitiveCompare($1.alias) == .orderedAscending },
            includePlatformRoots: includePlatformRootsValue
        )
    }
}
