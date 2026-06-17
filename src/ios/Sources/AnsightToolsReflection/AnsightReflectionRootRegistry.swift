import AnsightCore
import Foundation

public enum AnsightReflectionRootReferenceType: String, Sendable, Codable, CaseIterable {
    case weak
    case strong
    case getter
}

public struct AnsightReflectionRootMetadata: Sendable, Codable, Equatable {
    public var displayName: String
    public var description: String?
    public var hints: [String]

    public init(displayName: String, description: String? = nil, hints: [String] = []) {
        self.displayName = displayName
        self.description = description
        self.hints = hints
    }
}

public protocol AnsightReflectionMutableRoot: AnyObject {
    func setReflectionValue(path: String, value: JSONValue) throws -> JSONValue?
}

public protocol AnsightReflectionInvokableRoot: AnyObject {
    func invokeReflectionMethod(targetPath: String?, method: String, arguments: [JSONValue]) throws -> JSONValue?
}

public enum AnsightReflectionRootRegistry {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var roots: [String: RegisteredAnsightReflectionRoot] = [:]

    @discardableResult
    public static func register(
        id: String,
        target: AnyObject,
        metadata: AnsightReflectionRootMetadata,
        referenceType: AnsightReflectionRootReferenceType = .weak
    ) throws -> AnsightReflectionRootRegistrationHandle {
        let normalizedId = try normalizeId(id)
        let normalizedMetadata = try normalizeMetadata(metadata, fallbackDisplayName: normalizedId)
        let registrationId = UUID()
        let resolver: () -> Any?
        let storedReferenceType: AnsightReflectionRootReferenceType

        switch referenceType {
        case .weak:
            let box = WeakReflectionObjectBox(target)
            resolver = { box.value }
            storedReferenceType = .weak
        case .strong:
            let box = StrongReflectionObjectBox(target)
            resolver = { box.value }
            storedReferenceType = .strong
        case .getter:
            throw AnsightReflectionToolError.invalidArgument("Use registerGetter for getter reflection roots.")
        }

        let root = RegisteredAnsightReflectionRoot(
            id: normalizedId,
            registrationId: registrationId,
            metadata: normalizedMetadata,
            referenceType: storedReferenceType,
            resolve: resolver
        )

        lock.withLock {
            roots[normalizedId] = root
        }

        return AnsightReflectionRootRegistrationHandle(id: normalizedId, registrationId: registrationId)
    }

    @discardableResult
    public static func register(
        id: String,
        target: AnyObject,
        displayName: String? = nil,
        description: String? = nil,
        referenceType: AnsightReflectionRootReferenceType = .weak
    ) throws -> AnsightReflectionRootRegistrationHandle {
        try register(
            id: id,
            target: target,
            metadata: AnsightReflectionRootMetadata(
                displayName: displayName ?? id,
                description: description
            ),
            referenceType: referenceType
        )
    }

    @discardableResult
    public static func registerGetter(
        id: String,
        metadata: AnsightReflectionRootMetadata,
        getter: @escaping () -> Any?
    ) throws -> AnsightReflectionRootRegistrationHandle {
        let normalizedId = try normalizeId(id)
        let normalizedMetadata = try normalizeMetadata(metadata, fallbackDisplayName: normalizedId)
        let registrationId = UUID()
        let root = RegisteredAnsightReflectionRoot(
            id: normalizedId,
            registrationId: registrationId,
            metadata: normalizedMetadata,
            referenceType: .getter,
            resolve: getter
        )

        lock.withLock {
            roots[normalizedId] = root
        }

        return AnsightReflectionRootRegistrationHandle(id: normalizedId, registrationId: registrationId)
    }

    @discardableResult
    public static func registerGetter(
        id: String,
        displayName: String? = nil,
        description: String? = nil,
        getter: @escaping () -> Any?
    ) throws -> AnsightReflectionRootRegistrationHandle {
        try registerGetter(
            id: id,
            metadata: AnsightReflectionRootMetadata(displayName: displayName ?? id, description: description),
            getter: getter
        )
    }

    @discardableResult
    public static func deregister(_ id: String) -> Bool {
        guard let normalizedId = try? normalizeId(id) else {
            return false
        }

        return lock.withLock {
            roots.removeValue(forKey: normalizedId) != nil
        }
    }

    public static func clear() {
        lock.withLock {
            roots.removeAll()
        }
    }

    internal static func snapshot() -> [RegisteredAnsightReflectionRoot] {
        lock.withLock {
            roots.values.sorted { $0.id.localizedCaseInsensitiveCompare($1.id) == .orderedAscending }
        }
    }

    internal static func deregister(id: String, registrationId: UUID) -> Bool {
        lock.withLock {
            guard let current = roots[id], current.registrationId == registrationId else {
                return false
            }

            roots.removeValue(forKey: id)
            return true
        }
    }

    private static func normalizeId(_ id: String) throws -> String {
        let normalized = id.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalized.isEmpty else {
            throw AnsightReflectionToolError.invalidArgument("Reflection root id must not be blank.")
        }

        return normalized
    }

    private static func normalizeMetadata(
        _ metadata: AnsightReflectionRootMetadata,
        fallbackDisplayName: String
    ) throws -> AnsightReflectionRootMetadata {
        let displayName = metadata.displayName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !displayName.isEmpty else {
            throw AnsightReflectionToolError.invalidArgument("Reflection root metadata must include a display name.")
        }

        return AnsightReflectionRootMetadata(
            displayName: displayName.isEmpty ? fallbackDisplayName : displayName,
            description: metadata.description?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfBlank,
            hints: metadata.hints
                .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
                .filter { !$0.isEmpty }
        )
    }
}

public final class AnsightReflectionRootRegistrationHandle: @unchecked Sendable {
    public let id: String
    private let registrationId: UUID
    private let lock = NSLock()
    private var deregistered = false

    internal init(id: String, registrationId: UUID) {
        self.id = id
        self.registrationId = registrationId
    }

    @discardableResult
    public func deregister() -> Bool {
        let shouldDeregister = lock.withLock { () -> Bool in
            guard !deregistered else {
                return false
            }

            deregistered = true
            return true
        }

        guard shouldDeregister else {
            return false
        }

        return AnsightReflectionRootRegistry.deregister(id: id, registrationId: registrationId)
    }

    deinit {
        _ = deregister()
    }
}

internal struct RegisteredAnsightReflectionRoot {
    let id: String
    let registrationId: UUID
    let metadata: AnsightReflectionRootMetadata
    let referenceType: AnsightReflectionRootReferenceType
    let resolve: () -> Any?
}

private final class WeakReflectionObjectBox: @unchecked Sendable {
    weak var value: AnyObject?

    init(_ value: AnyObject) {
        self.value = value
    }
}

private final class StrongReflectionObjectBox: @unchecked Sendable {
    let value: AnyObject

    init(_ value: AnyObject) {
        self.value = value
    }
}

private extension String {
    var nilIfBlank: String? {
        isEmpty ? nil : self
    }
}
