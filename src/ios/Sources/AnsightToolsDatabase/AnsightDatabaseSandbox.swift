import AnsightCore
import Foundation

internal enum AnsightDatabaseSandbox {
    static func roots(options: AnsightDatabaseToolsOptions) throws -> [AnsightDatabaseRoot] {
        var roots: [AnsightDatabaseRoot] = []
        if options.includePlatformRoots {
            try platformRoots().forEach { try addRoot($0, to: &roots) }
        }
        try options.additionalRoots.forEach { try addRoot($0, to: &roots) }

        if roots.isEmpty {
            throw AnsightDatabaseToolError.operationFailed("No database sandbox roots are available for the current app.")
        }

        return roots
    }

    static func resolveDatabasePath(arguments: [String: String], roots: [AnsightDatabaseRoot]) throws -> AnsightDatabaseResolvedPath {
        let requestedPath = AnsightDatabaseArgumentReader.string(arguments, key: "path")
            ?? AnsightDatabaseArgumentReader.string(arguments, key: "database")

        guard let requestedPath else {
            throw AnsightDatabaseToolError.invalidArgument(
                "The database tools require a 'path' argument that points to a SQLite database inside the app sandbox."
            )
        }

        if isAbsolutePath(requestedPath) {
            let fullPath = canonicalPath(requestedPath)
            guard let root = containingRoot(roots: roots, fullPath: fullPath) else {
                throw AnsightDatabaseToolError.notAllowed("The path '\(fullPath)' is outside the approved app sandbox roots.")
            }

            try ensureUsableDatabase(path: fullPath)
            return AnsightDatabaseResolvedPath(rootAlias: root.alias, rootPath: root.path, fullPath: fullPath)
        }

        if let match = try existingRelativePathMatch(roots: roots, requestedPath: requestedPath) {
            let fullPath = canonicalPath((match.path as NSString).appendingPathComponent(requestedPath))
            try ensureUsableDatabase(path: fullPath)
            return AnsightDatabaseResolvedPath(rootAlias: match.alias, rootPath: match.path, fullPath: fullPath)
        }

        let root = selectDefaultRoot(roots: roots)
        let fullPath = canonicalPath((root.path as NSString).appendingPathComponent(requestedPath))
        try ensureWithinRoot(fullPath: fullPath, rootPath: root.path)
        try ensureUsableDatabase(path: fullPath)
        return AnsightDatabaseResolvedPath(rootAlias: root.alias, rootPath: root.path, fullPath: fullPath)
    }

    static func availableRootsJSON(_ roots: [AnsightDatabaseRoot]) -> JSONValue {
        .array(roots.map { root in
            .object([
                "alias": .string(root.alias),
                "path": .string(root.path),
            ])
        })
    }

    static func databaseEntryJSON(path: String, root: AnsightDatabaseRoot) throws -> JSONValue {
        let attributes = try FileManager.default.attributesOfItem(atPath: path)
        let modified = attributes[.modificationDate] as? Date ?? Date(timeIntervalSince1970: 0)
        let sizeBytes = (attributes[.size] as? NSNumber)?.int64Value ?? 0

        return .object([
            "name": .string((path as NSString).lastPathComponent),
            "path": .string(path),
            "relativePath": .string(relativePath(rootPath: root.path, fullPath: path)),
            "rootAlias": .string(root.alias),
            "sizeBytes": .integer(sizeBytes),
            "lastModifiedUtc": .string(AnsightClock.isoString(from: modified)),
        ])
    }

    static func looksLikeSqliteDatabase(_ path: String) -> Bool {
        guard let handle = FileHandle(forReadingAtPath: path) else {
            return false
        }
        defer {
            try? handle.close()
        }

        let header = handle.readData(ofLength: 16)
        guard header.count == 16,
              let text = String(data: header, encoding: .ascii) else {
            return false
        }

        return text.hasPrefix("SQLite format 3")
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

    static func canonicalPath(_ path: String) -> String {
        URL(fileURLWithPath: path).standardizedFileURL.resolvingSymlinksInPath().path
    }

    static func isSystemStorePath(_ path: String) -> Bool {
        path.range(of: "/Caches/", options: [.caseInsensitive]) != nil
    }

    private static func platformRoots() -> [AnsightDatabaseRoot] {
        var roots: [AnsightDatabaseRoot] = []
        if let library = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask).first {
            roots.append(AnsightDatabaseRoot(alias: "appData", path: library.path))
        }

        if let documents = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first {
            roots.append(AnsightDatabaseRoot(alias: "documents", path: documents.path))
        }

        if let cache = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first {
            roots.append(AnsightDatabaseRoot(alias: "cache", path: cache.path))
        }

        roots.append(AnsightDatabaseRoot(alias: "temp", path: NSTemporaryDirectory()))
        return roots
    }

    private static func addRoot(_ root: AnsightDatabaseRoot, to roots: inout [AnsightDatabaseRoot]) throws {
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

            throw AnsightDatabaseToolError.invalidArgument(
                "A database sandbox root with alias '\(root.alias)' is already registered for '\(existing.path)'."
            )
        }

        if roots.contains(where: { $0.path == fullPath }) {
            return
        }

        roots.append(AnsightDatabaseRoot(alias: root.alias, path: fullPath))
    }

    private static func existingRelativePathMatch(
        roots: [AnsightDatabaseRoot],
        requestedPath: String
    ) throws -> AnsightDatabaseRoot? {
        let matches = roots.filter { root in
            let candidate = canonicalPath((root.path as NSString).appendingPathComponent(requestedPath))
            guard isWithinRoot(fullPath: candidate, rootPath: root.path) else {
                return false
            }

            var isDirectory: ObjCBool = false
            return FileManager.default.fileExists(atPath: candidate, isDirectory: &isDirectory) && !isDirectory.boolValue
        }

        if matches.count > 1 {
            throw AnsightDatabaseToolError.invalidArgument(
                "The database path '\(requestedPath)' exists in multiple approved sandbox roots. Use an absolute path or root-specific path instead."
            )
        }

        return matches.first
    }

    private static func selectDefaultRoot(roots: [AnsightDatabaseRoot]) -> AnsightDatabaseRoot {
        if let appData = roots.first(where: { $0.alias.caseInsensitiveCompare("appData") == .orderedSame }) {
            return appData
        }

        return roots[0]
    }

    private static func containingRoot(roots: [AnsightDatabaseRoot], fullPath: String) -> AnsightDatabaseRoot? {
        roots.first { isWithinRoot(fullPath: fullPath, rootPath: $0.path) }
    }

    private static func ensureUsableDatabase(path: String) throws {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory), !isDirectory.boolValue else {
            throw AnsightDatabaseToolError.notFound("The database '\(path)' does not exist.")
        }

        guard looksLikeSqliteDatabase(path) else {
            throw AnsightDatabaseToolError.invalidArgument("The file '\(path)' is not recognized as a SQLite database.")
        }
    }

    private static func ensureWithinRoot(fullPath: String, rootPath: String) throws {
        guard isWithinRoot(fullPath: fullPath, rootPath: rootPath) else {
            throw AnsightDatabaseToolError.notAllowed("The path '\(fullPath)' is outside the approved app sandbox root '\(rootPath)'.")
        }
    }

    private static func isWithinRoot(fullPath: String, rootPath: String) -> Bool {
        if fullPath == rootPath {
            return true
        }

        let normalizedRoot = rootPath.hasSuffix("/") ? rootPath : "\(rootPath)/"
        return fullPath.hasPrefix(normalizedRoot)
    }

    private static func isAbsolutePath(_ path: String) -> Bool {
        (path as NSString).isAbsolutePath
    }
}
