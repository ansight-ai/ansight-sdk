import Foundation

public struct AnsightReflectionToolsOptions: Sendable, Codable, Equatable {
    public static let `default` = AnsightReflectionToolsOptions()

    public let includeBuiltInRoots: Bool
    public let allowedRootIds: [String]
    public let allowedTypePrefixes: [String]

    public init(
        includeBuiltInRoots: Bool = true,
        allowedRootIds: [String] = [],
        allowedTypePrefixes: [String] = []
    ) {
        self.includeBuiltInRoots = includeBuiltInRoots
        self.allowedRootIds = allowedRootIds.compactMap(Self.normalized)
        self.allowedTypePrefixes = allowedTypePrefixes.compactMap(Self.normalized)
    }

    public static func createBuilder() -> AnsightReflectionToolsOptionsBuilder {
        AnsightReflectionToolsOptionsBuilder()
    }

    public func isRootAllowed(_ rootId: String) -> Bool {
        allowedRootIds.isEmpty || allowedRootIds.contains(rootId)
    }

    public func isTypeAllowed(_ typeName: String) -> Bool {
        allowedTypePrefixes.isEmpty || allowedTypePrefixes.contains { typeName.hasPrefix($0) }
    }

    private static func normalized(_ value: String?) -> String? {
        guard let value else {
            return nil
        }

        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }
}

public final class AnsightReflectionToolsOptionsBuilder {
    private var includeBuiltInRootsValue = true
    private var allowedRootIds: [String] = []
    private var allowedTypePrefixes: [String] = []

    public init() {}

    @discardableResult
    public func includeBuiltInRoots(_ includeBuiltInRoots: Bool) -> AnsightReflectionToolsOptionsBuilder {
        includeBuiltInRootsValue = includeBuiltInRoots
        return self
    }

    @discardableResult
    public func allowRoot(_ rootId: String) -> AnsightReflectionToolsOptionsBuilder {
        let normalized = rootId.trimmingCharacters(in: .whitespacesAndNewlines)
        if !normalized.isEmpty {
            allowedRootIds.append(normalized)
        }

        return self
    }

    @discardableResult
    public func allowRoots(_ rootIds: [String]) -> AnsightReflectionToolsOptionsBuilder {
        rootIds.forEach { allowRoot($0) }
        return self
    }

    @discardableResult
    public func allowTypePrefix(_ typePrefix: String) -> AnsightReflectionToolsOptionsBuilder {
        let normalized = typePrefix.trimmingCharacters(in: .whitespacesAndNewlines)
        if !normalized.isEmpty {
            allowedTypePrefixes.append(normalized)
        }

        return self
    }

    @discardableResult
    public func allowTypePrefixes(_ typePrefixes: [String]) -> AnsightReflectionToolsOptionsBuilder {
        typePrefixes.forEach { allowTypePrefix($0) }
        return self
    }

    public func build() -> AnsightReflectionToolsOptions {
        AnsightReflectionToolsOptions(
            includeBuiltInRoots: includeBuiltInRootsValue,
            allowedRootIds: Array(Set(allowedRootIds)).sorted(),
            allowedTypePrefixes: Array(Set(allowedTypePrefixes)).sorted()
        )
    }
}
