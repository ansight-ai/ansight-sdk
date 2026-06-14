import AnsightKit
import Foundation

public final class ListDirectoryTool: AnsightTool {
    private let options: AnsightFileSystemToolsOptions

    public init(options: AnsightFileSystemToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightFileSystemToolIds.listDirectory,
            name: "List Directory",
            description: "Lists files and folders inside the app sandbox.",
            category: "files",
            scope: AnsightToolScope.read.rawValue,
            keywords: "filesystem files directory sandbox",
            security: AnsightFileSystemToolSecurityProfiles.listDirectory,
            argumentsSchema: AnsightFileSystemToolSchemas.listDirectoryArguments,
            resultSchema: AnsightFileSystemToolSchemas.listDirectoryResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let roots = try AnsightFileSystemSandbox.roots(options: options)
            let resolvedDirectory = try AnsightFileSystemSandbox.resolvePath(
                arguments: arguments,
                roots: roots,
                requireExisting: true,
                expectDirectory: true
            )
            let includeHidden = try AnsightFileSystemSandbox.boolean(arguments, key: "includeHidden", defaultValue: false)
            let recursive = try AnsightFileSystemSandbox.boolean(arguments, key: "recursive", defaultValue: false)
            let maxDepth = recursive
                ? try AnsightFileSystemSandbox.integer(arguments, key: "maxDepth", defaultValue: 5, minimum: 1, maximum: 16)
                : try AnsightFileSystemSandbox.integer(arguments, key: "maxDepth", defaultValue: 1, minimum: 1, maximum: 1)
            let maxEntries = try AnsightFileSystemSandbox.integer(arguments, key: "maxEntries", defaultValue: 200, minimum: 1, maximum: 1_000)

            var entries: [JSONValue] = []
            var pending: [(path: String, depth: Int)] = [(resolvedDirectory.fullPath, 0)]

            while !pending.isEmpty && entries.count < maxEntries {
                let current = pending.removeFirst()
                let children = (try? AnsightFileSystemSandbox.sortedDirectoryEntries(atPath: current.path)) ?? (directories: [], files: [])

                for directory in children.directories {
                    if !includeHidden && AnsightFileSystemSandbox.isHidden(path: directory) {
                        continue
                    }

                    entries.append(try AnsightFileSystemSandbox.entryJSON(
                        path: directory,
                        rootAlias: resolvedDirectory.rootAlias,
                        rootPath: resolvedDirectory.rootPath
                    ))

                    if current.depth + 1 < maxDepth {
                        pending.append((directory, current.depth + 1))
                    }

                    if entries.count >= maxEntries {
                        break
                    }
                }

                if entries.count >= maxEntries {
                    break
                }

                for file in children.files {
                    if !includeHidden && AnsightFileSystemSandbox.isHidden(path: file) {
                        continue
                    }

                    entries.append(try AnsightFileSystemSandbox.entryJSON(
                        path: file,
                        rootAlias: resolvedDirectory.rootAlias,
                        rootPath: resolvedDirectory.rootPath
                    ))

                    if entries.count >= maxEntries {
                        break
                    }
                }
            }

            return .success(.object([
                "rootAlias": .string(resolvedDirectory.rootAlias),
                "rootPath": .string(resolvedDirectory.rootPath),
                "directoryPath": .string(resolvedDirectory.fullPath),
                "relativePath": .string(AnsightFileSystemSandbox.relativePath(
                    rootPath: resolvedDirectory.rootPath,
                    fullPath: resolvedDirectory.fullPath
                )),
                "availableRoots": AnsightFileSystemSandbox.availableRootsJSON(roots),
                "entries": .array(entries),
                "truncated": .bool(entries.count >= maxEntries),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
            ]))
        } catch {
            return .failure(error.localizedDescription, errorCode: "filesystem_list_failed")
        }
    }
}
