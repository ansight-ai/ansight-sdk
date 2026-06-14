import AnsightKit
import Foundation

internal enum AnsightFileSystemSandbox {
    static func roots(options: AnsightFileSystemToolsOptions) throws -> [AnsightFileSystemRoot] {
        var roots: [AnsightFileSystemRoot] = []
        try platformRoots().forEach { try addRoot($0, to: &roots) }
        try options.additionalRoots.forEach { try addRoot($0, to: &roots) }

        if roots.isEmpty {
            throw AnsightFileSystemToolError.operationFailed("No sandbox roots are available for the current app.")
        }

        return roots
    }

    static func resolvePath(
        arguments: [String: String],
        roots: [AnsightFileSystemRoot],
        rootKey: String = "root",
        pathKey: String = "path",
        requireExisting: Bool,
        expectDirectory: Bool
    ) throws -> AnsightFileSystemResolvedPath {
        let requestedPath = string(arguments, key: pathKey)
        let requestedRoot = string(arguments, key: rootKey)

        if let requestedRoot {
            let root = try resolveRoot(roots: roots, requestedRoot: requestedRoot)
            let fullPath = canonicalPath(buildFullPath(rootPath: root.path, requestedPath: requestedPath))
            try ensureWithinRoot(fullPath: fullPath, rootPath: root.path)
            try ensurePathExists(
                fullPath: fullPath,
                requireExisting: requireExisting,
                expectDirectory: expectDirectory,
                requestedPath: requestedPath,
                searchAllRoots: false
            )
            return AnsightFileSystemResolvedPath(rootAlias: root.alias, rootPath: root.path, fullPath: fullPath)
        }

        guard let requestedPath else {
            let root = selectDefaultRoot(roots: roots)
            return AnsightFileSystemResolvedPath(rootAlias: root.alias, rootPath: root.path, fullPath: root.path)
        }

        if isAbsolutePath(requestedPath) {
            let fullPath = canonicalPath(requestedPath)
            guard let root = containingRoot(roots: roots, fullPath: fullPath) else {
                throw AnsightFileSystemToolError.notAllowed("The path '\(fullPath)' is outside the approved app sandbox roots.")
            }

            try ensurePathExists(
                fullPath: fullPath,
                requireExisting: requireExisting,
                expectDirectory: expectDirectory,
                requestedPath: requestedPath,
                searchAllRoots: false
            )
            return AnsightFileSystemResolvedPath(rootAlias: root.alias, rootPath: root.path, fullPath: fullPath)
        }

        if let match = try existingRelativePathMatch(roots: roots, requestedPath: requestedPath, expectDirectory: expectDirectory) {
            return AnsightFileSystemResolvedPath(
                rootAlias: match.alias,
                rootPath: match.path,
                fullPath: canonicalPath((match.path as NSString).appendingPathComponent(requestedPath))
            )
        }

        let root = selectDefaultRoot(roots: roots)
        let fullPath = canonicalPath((root.path as NSString).appendingPathComponent(requestedPath))
        try ensurePathExists(
            fullPath: fullPath,
            requireExisting: requireExisting,
            expectDirectory: expectDirectory,
            requestedPath: requestedPath,
            searchAllRoots: true
        )
        return AnsightFileSystemResolvedPath(rootAlias: root.alias, rootPath: root.path, fullPath: fullPath)
    }

    static func resolveDestinationPath(
        arguments: [String: String],
        roots: [AnsightFileSystemRoot],
        sourceRoot: AnsightFileSystemRoot,
        destinationRootKey: String = "destinationRoot",
        destinationPathKey: String = "destinationPath"
    ) throws -> AnsightFileSystemResolvedPath {
        let destinationPath = try requiredString(arguments, key: destinationPathKey)

        if isAbsolutePath(destinationPath) {
            let fullPath = canonicalPath(destinationPath)
            guard let root = containingRoot(roots: roots, fullPath: fullPath) else {
                throw AnsightFileSystemToolError.notAllowed("The path '\(fullPath)' is outside the approved app sandbox roots.")
            }

            return AnsightFileSystemResolvedPath(rootAlias: root.alias, rootPath: root.path, fullPath: fullPath)
        }

        let root = try string(arguments, key: destinationRootKey).map {
            try resolveRoot(roots: roots, requestedRoot: $0)
        } ?? sourceRoot
        let fullPath = canonicalPath((root.path as NSString).appendingPathComponent(destinationPath))
        try ensureWithinRoot(fullPath: fullPath, rootPath: root.path)
        return AnsightFileSystemResolvedPath(rootAlias: root.alias, rootPath: root.path, fullPath: fullPath)
    }

    static func availableRootsJSON(_ roots: [AnsightFileSystemRoot]) -> JSONValue {
        .array(roots.map { root in
            .object([
                "alias": .string(root.alias),
                "path": .string(root.path),
            ])
        })
    }

    static func entryJSON(path: String, rootAlias: String, rootPath: String) throws -> JSONValue {
        let attributes = try FileManager.default.attributesOfItem(atPath: path)
        let isDirectory = isDirectory(attributes: attributes)
        let fileName = (path as NSString).lastPathComponent
        let sizeBytes = isDirectory ? JSONValue.null : .integer(Int64((attributes[.size] as? NSNumber)?.int64Value ?? 0))
        let fileExtension = isDirectory ? JSONValue.null : optionalString(AnsightFileSystemContentDescriptor.fileExtension(path: path))
        let mimeType = isDirectory ? JSONValue.null : .string(AnsightFileSystemContentDescriptor.mimeType(path: path))
        let modified = attributes[.modificationDate] as? Date ?? Date(timeIntervalSince1970: 0)

        return .object([
            "name": .string(fileName),
            "path": .string(path),
            "relativePath": .string(relativePath(rootPath: rootPath, fullPath: path)),
            "rootAlias": .string(rootAlias),
            "kind": .string(isDirectory ? "directory" : "file"),
            "sizeBytes": sizeBytes,
            "fileExtension": fileExtension,
            "mimeType": mimeType,
            "lastModifiedUtc": .string(AnsightClock.isoString(from: modified)),
            "isHidden": .bool(isHidden(path: path)),
        ])
    }

    static func sortedDirectoryEntries(atPath path: String) throws -> (directories: [String], files: [String]) {
        let urls = try FileManager.default.contentsOfDirectory(
            at: URL(fileURLWithPath: path),
            includingPropertiesForKeys: [.isDirectoryKey],
            options: []
        )

        var directories: [String] = []
        var files: [String] = []
        for url in urls {
            let resourceValues = try? url.resourceValues(forKeys: [.isDirectoryKey])
            if resourceValues?.isDirectory == true {
                directories.append(canonicalPath(url.path))
            } else {
                files.append(canonicalPath(url.path))
            }
        }

        directories.sort { ($0 as NSString).lastPathComponent.localizedCaseInsensitiveCompare(($1 as NSString).lastPathComponent) == .orderedAscending }
        files.sort { ($0 as NSString).lastPathComponent.localizedCaseInsensitiveCompare(($1 as NSString).lastPathComponent) == .orderedAscending }
        return (directories, files)
    }

    static func integer(
        _ arguments: [String: String],
        key: String,
        defaultValue: Int,
        minimum: Int,
        maximum: Int
    ) throws -> Int {
        guard let rawValue = string(arguments, key: key) else {
            return defaultValue
        }

        guard let value = Int(rawValue) else {
            throw AnsightFileSystemToolError.invalidArgument("The argument '\(key)' must be an integer.")
        }

        return min(max(value, minimum), maximum)
    }

    static func int64(
        _ arguments: [String: String],
        key: String,
        defaultValue: Int64,
        minimum: Int64,
        maximum: Int64
    ) throws -> Int64 {
        guard let rawValue = string(arguments, key: key) else {
            return defaultValue
        }

        guard let value = Int64(rawValue) else {
            throw AnsightFileSystemToolError.invalidArgument("The argument '\(key)' must be an integer.")
        }

        return min(max(value, minimum), maximum)
    }

    static func boolean(_ arguments: [String: String], key: String, defaultValue: Bool) throws -> Bool {
        guard let rawValue = string(arguments, key: key) else {
            return defaultValue
        }

        switch rawValue.lowercased() {
        case "true", "1":
            return true
        case "false", "0":
            return false
        default:
            throw AnsightFileSystemToolError.invalidArgument("The argument '\(key)' must be a boolean.")
        }
    }

    static func string(_ arguments: [String: String], key: String) -> String? {
        guard let rawValue = arguments[key] else {
            return nil
        }

        let value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }

    static func requiredString(_ arguments: [String: String], key: String) throws -> String {
        guard let value = string(arguments, key: key) else {
            throw AnsightFileSystemToolError.invalidArgument("The argument '\(key)' is required.")
        }

        return value
    }

    static func ensureWritableDestination(_ destination: AnsightFileSystemResolvedPath, overwrite: Bool, createDirectory: Bool) throws -> Bool {
        let parent = (destination.fullPath as NSString).deletingLastPathComponent
        var isDirectory: ObjCBool = false
        let parentExists = FileManager.default.fileExists(atPath: parent, isDirectory: &isDirectory)
        var createdDirectory = false

        if !parentExists {
            guard createDirectory else {
                throw AnsightFileSystemToolError.notFound("The destination directory '\(parent)' does not exist.")
            }

            try FileManager.default.createDirectory(atPath: parent, withIntermediateDirectories: true)
            createdDirectory = true
        } else if !isDirectory.boolValue {
            throw AnsightFileSystemToolError.invalidArgument("The destination parent '\(parent)' is not a directory.")
        }

        if FileManager.default.fileExists(atPath: destination.fullPath) && !overwrite {
            throw AnsightFileSystemToolError.invalidArgument("The destination file '\(destination.fullPath)' already exists.")
        }

        return createdDirectory
    }

    static func root(for resolvedPath: AnsightFileSystemResolvedPath) -> AnsightFileSystemRoot {
        AnsightFileSystemRoot(alias: resolvedPath.rootAlias, path: resolvedPath.rootPath)
    }

    static func relativePath(rootPath: String, fullPath: String) -> String {
        let rootComponents = URL(fileURLWithPath: rootPath).standardized.pathComponents
        let pathComponents = URL(fileURLWithPath: fullPath).standardized.pathComponents
        guard pathComponents.count >= rootComponents.count,
              Array(pathComponents.prefix(rootComponents.count)) == rootComponents else {
            return fullPath
        }

        let relativeComponents = pathComponents.dropFirst(rootComponents.count)
        return relativeComponents.isEmpty ? "." : relativeComponents.joined(separator: "/")
    }

    static func optionalString(_ value: String?) -> JSONValue {
        value.map(JSONValue.string) ?? .null
    }

    static func canonicalPath(_ path: String) -> String {
        URL(fileURLWithPath: path).standardizedFileURL.resolvingSymlinksInPath().path
    }

    static func isHidden(path: String) -> Bool {
        (path as NSString).lastPathComponent.hasPrefix(".")
    }

    private static func platformRoots() -> [AnsightFileSystemRoot] {
        var roots: [AnsightFileSystemRoot] = []
        if let library = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask).first {
            roots.append(AnsightFileSystemRoot(alias: "appData", path: library.path))
        }

        if let documents = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first {
            roots.append(AnsightFileSystemRoot(alias: "documents", path: documents.path))
        }

        if let cache = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first {
            roots.append(AnsightFileSystemRoot(alias: "cache", path: cache.path))
        }

        roots.append(AnsightFileSystemRoot(alias: "temp", path: NSTemporaryDirectory()))
        return roots
    }

    private static func addRoot(_ root: AnsightFileSystemRoot, to roots: inout [AnsightFileSystemRoot]) throws {
        guard !root.alias.isEmpty, !root.path.isEmpty else {
            return
        }

        let fullPath = canonicalPath(root.path)
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: fullPath, isDirectory: &isDirectory), isDirectory.boolValue else {
            return
        }

        if let existing = roots.first(where: { $0.alias.caseInsensitiveCompare(root.alias) == .orderedSame }) {
            if existing.path == fullPath {
                return
            }

            throw AnsightFileSystemToolError.invalidArgument(
                "A sandbox root with alias '\(root.alias)' is already registered for '\(existing.path)'."
            )
        }

        if roots.contains(where: { $0.path == fullPath }) {
            return
        }

        roots.append(AnsightFileSystemRoot(alias: root.alias, path: fullPath))
    }

    private static func buildFullPath(rootPath: String, requestedPath: String?) -> String {
        guard let requestedPath else {
            return rootPath
        }

        if isAbsolutePath(requestedPath) {
            return requestedPath
        }

        return (rootPath as NSString).appendingPathComponent(requestedPath)
    }

    private static func resolveRoot(roots: [AnsightFileSystemRoot], requestedRoot: String) throws -> AnsightFileSystemRoot {
        if let root = roots.first(where: { $0.alias.caseInsensitiveCompare(requestedRoot) == .orderedSame }) {
            return root
        }

        let requestedPath = canonicalPath(requestedRoot)
        if let root = roots.first(where: { $0.path == requestedPath }) {
            return root
        }

        throw AnsightFileSystemToolError.notAllowed("The sandbox root '\(requestedRoot)' is not available.")
    }

    private static func selectDefaultRoot(roots: [AnsightFileSystemRoot]) -> AnsightFileSystemRoot {
        if let appData = roots.first(where: { $0.alias.caseInsensitiveCompare("appData") == .orderedSame }) {
            return appData
        }

        if let populated = roots.first(where: { rootHasEntries($0.path) }) {
            return populated
        }

        return roots[0]
    }

    private static func containingRoot(roots: [AnsightFileSystemRoot], fullPath: String) -> AnsightFileSystemRoot? {
        roots.first { isWithinRoot(fullPath: fullPath, rootPath: $0.path) }
    }

    private static func existingRelativePathMatch(
        roots: [AnsightFileSystemRoot],
        requestedPath: String,
        expectDirectory: Bool
    ) throws -> AnsightFileSystemRoot? {
        let matches = roots.filter { root in
            let candidate = canonicalPath((root.path as NSString).appendingPathComponent(requestedPath))
            guard isWithinRoot(fullPath: candidate, rootPath: root.path) else {
                return false
            }

            return pathExists(candidate, expectDirectory: expectDirectory)
        }

        if matches.count > 1 {
            throw AnsightFileSystemToolError.invalidArgument(
                "The relative path '\(requestedPath)' exists in multiple approved sandbox roots. Specify the 'root' argument explicitly."
            )
        }

        return matches.first
    }

    private static func ensurePathExists(
        fullPath: String,
        requireExisting: Bool,
        expectDirectory: Bool,
        requestedPath: String?,
        searchAllRoots: Bool
    ) throws {
        guard requireExisting else {
            return
        }

        if pathExists(fullPath, expectDirectory: expectDirectory) {
            return
        }

        let label = expectDirectory ? "directory" : "file"
        if searchAllRoots, let requestedPath {
            throw AnsightFileSystemToolError.notFound("The \(label) '\(requestedPath)' was not found in any approved app sandbox root.")
        }

        throw AnsightFileSystemToolError.notFound("The \(label) '\(fullPath)' does not exist.")
    }

    private static func pathExists(_ path: String, expectDirectory: Bool) -> Bool {
        var isDirectory: ObjCBool = false
        let exists = FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory)
        return exists && isDirectory.boolValue == expectDirectory
    }

    private static func ensureWithinRoot(fullPath: String, rootPath: String) throws {
        guard isWithinRoot(fullPath: fullPath, rootPath: rootPath) else {
            throw AnsightFileSystemToolError.notAllowed("The path '\(fullPath)' is outside the approved app sandbox root '\(rootPath)'.")
        }
    }

    private static func isWithinRoot(fullPath: String, rootPath: String) -> Bool {
        if fullPath == rootPath {
            return true
        }

        let normalizedRoot = rootPath.hasSuffix("/") ? rootPath : "\(rootPath)/"
        return fullPath.hasPrefix(normalizedRoot)
    }

    private static func rootHasEntries(_ path: String) -> Bool {
        ((try? FileManager.default.contentsOfDirectory(atPath: path).isEmpty) == false)
    }

    private static func isAbsolutePath(_ path: String) -> Bool {
        (path as NSString).isAbsolutePath
    }

    private static func isDirectory(attributes: [FileAttributeKey: Any]) -> Bool {
        (attributes[.type] as? FileAttributeType) == .typeDirectory
    }
}
