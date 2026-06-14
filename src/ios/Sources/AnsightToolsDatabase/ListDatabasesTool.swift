import AnsightKit
import Foundation

public final class ListDatabasesTool: AnsightTool {
    private let options: AnsightDatabaseToolsOptions

    public init(options: AnsightDatabaseToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightDatabaseToolIds.listDatabases,
            name: "List Databases",
            description: "Lists the known app databases that can be inspected.",
            category: "data",
            scope: AnsightToolScope.read.rawValue,
            keywords: "database sqlite storage schema",
            security: AnsightDatabaseToolSecurityProfiles.listDatabases,
            argumentsSchema: AnsightDatabaseToolSchemas.listDatabasesArguments,
            resultSchema: AnsightDatabaseToolSchemas.listDatabasesResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let includeSystemStores = try AnsightDatabaseArgumentReader.boolean(
                arguments,
                key: "includeSystemStores",
                defaultValue: false
            )
            let maxResults = try AnsightDatabaseArgumentReader.integer(
                arguments,
                key: "maxResults",
                defaultValue: 200,
                minimum: 1,
                maximum: 1_000
            )

            let roots = try AnsightDatabaseSandbox.roots(options: options)
            let databases = try listDatabases(
                roots: roots,
                includeSystemStores: includeSystemStores,
                maxResults: maxResults
            )

            return .success(.object([
                "databases": .array(databases.entries),
                "truncated": .bool(databases.truncated),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
            ]))
        } catch {
            return .failure(error.localizedDescription, errorCode: "database_list_failed")
        }
    }

    private func listDatabases(
        roots: [AnsightDatabaseRoot],
        includeSystemStores: Bool,
        maxResults: Int
    ) throws -> (entries: [JSONValue], truncated: Bool) {
        var entries: [JSONValue] = []
        var seenPaths: Set<String> = []

        for root in roots {
            var pending: [(path: String, depth: Int)] = [(root.path, 0)]
            while !pending.isEmpty {
                let current = pending.removeFirst()
                guard let children = try? FileManager.default.contentsOfDirectory(
                    at: URL(fileURLWithPath: current.path),
                    includingPropertiesForKeys: [.isDirectoryKey],
                    options: []
                ) else {
                    continue
                }

                for child in children.sorted(by: { $0.path.localizedCaseInsensitiveCompare($1.path) == .orderedAscending }) {
                    let childPath = AnsightDatabaseSandbox.canonicalPath(child.path)
                    let resourceValues = try? child.resourceValues(forKeys: [.isDirectoryKey])

                    if resourceValues?.isDirectory == true {
                        if current.depth < 8 {
                            pending.append((childPath, current.depth + 1))
                        }

                        continue
                    }

                    if !includeSystemStores && AnsightDatabaseSandbox.isSystemStorePath(childPath) {
                        continue
                    }

                    guard seenPaths.insert(childPath).inserted,
                          AnsightDatabaseSandbox.looksLikeSqliteDatabase(childPath)
                    else {
                        continue
                    }

                    entries.append(try AnsightDatabaseSandbox.databaseEntryJSON(path: childPath, root: root))
                    if entries.count >= maxResults {
                        return (entries, true)
                    }
                }
            }
        }

        return (entries, false)
    }
}
