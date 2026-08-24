import AnsightCore
import Foundation

internal struct AnsightVisualTreeSnapshot: Sendable {
    let snapshotId: String
    let source: String
    let revision: Int64
    let payload: [String: JSONValue]
    let nodeIds: Set<String>
}

internal enum AnsightVisualTreeSnapshotLookup {
    case success(AnsightVisualTreeSnapshot)
    case failure(AnsightToolExecutionResult)
}

internal enum AnsightVisualTreeSnapshotStore {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var nextRevision: Int64 = 0
    nonisolated(unsafe) private static var snapshots: [String: AnsightVisualTreeSnapshot] = [:]
    nonisolated(unsafe) private static var insertionOrder: [String] = []
    nonisolated(unsafe) private static var latestRevisions: [String: Int64] = [:]

    static func capture(source rawSource: String?, arguments: [String: String]) -> AnsightToolExecutionResult {
        let source = AnsightVisualTreeProviderRegistry.normalizedSourceOrDefault(rawSource)
        guard let provider = AnsightVisualTreeProviderRegistry.provider(for: source) else {
            return .failure(
                "No visual tree provider is registered for source '\(source)'.",
                errorCode: "visual_tree_provider_not_found"
            )
        }
        let result = provider.getVisualTree(arguments: arguments)
        guard result.success, case .object(var payload)? = result.result else { return result }

        let snapshot: AnsightVisualTreeSnapshot = lock.withLock {
            nextRevision += 1
            let revision = nextRevision
            let snapshotId = "\(source):\(revision):\(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased())"
            payload["source"] = .string(source)
            payload["snapshotId"] = .string(snapshotId)
            payload["revision"] = .integer(revision)
            payload["nodeIdentity"] = .object([
                "scope": .string("snapshot"),
                "source": .string(source),
                "staleAfterRevision": .integer(revision),
            ])
            let snapshot = AnsightVisualTreeSnapshot(
                snapshotId: snapshotId,
                source: source,
                revision: revision,
                payload: payload,
                nodeIds: collectNodeIds(payload["root"])
            )
            snapshots[snapshotId] = snapshot
            insertionOrder.append(snapshotId)
            latestRevisions[source] = revision
            while insertionOrder.count > 32 {
                snapshots.removeValue(forKey: insertionOrder.removeFirst())
            }
            return snapshot
        }
        return .success(.object(snapshot.payload), message: result.message)
    }

    static func current(snapshotId: String, source rawSource: String?) -> AnsightVisualTreeSnapshotLookup {
        lock.withLock {
            guard let snapshot = snapshots[snapshotId] else {
                return .failure(stale(snapshotId: snapshotId, source: rawSource ?? "native", message: "The referenced UI snapshot is unknown or has expired."))
            }
            let source = rawSource?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false
                ? AnsightVisualTreeProviderRegistry.normalizedSourceOrDefault(rawSource)
                : snapshot.source
            guard snapshot.source.caseInsensitiveCompare(source) == .orderedSame else {
                return .failure(stale(snapshotId: snapshotId, source: source, message: "Snapshot '\(snapshotId)' belongs to source '\(snapshot.source)'."))
            }
            guard latestRevisions[source] == snapshot.revision else {
                return .failure(stale(
                    snapshotId: snapshotId,
                    source: source,
                    message: "Snapshot '\(snapshotId)' was superseded by revision \(latestRevisions[source] ?? 0).",
                    latestRevision: latestRevisions[source]
                ))
            }
            return .success(snapshot)
        }
    }

    static func validateNode(
        snapshotId: String,
        source: String?,
        nodeId: String
    ) -> AnsightVisualTreeSnapshotLookup {
        switch current(snapshotId: snapshotId, source: source) {
        case .failure(let error): return .failure(error)
        case .success(let snapshot):
            guard snapshot.nodeIds.contains(nodeId) else {
                return .failure(stale(
                    snapshotId: snapshotId,
                    source: snapshot.source,
                    message: "Node '\(nodeId)' does not belong to snapshot '\(snapshotId)'.",
                    latestRevision: snapshot.revision,
                    nodeId: nodeId
                ))
            }
            return .success(snapshot)
        }
    }

    static func reference(snapshot: AnsightVisualTreeSnapshot, nodeId: String) -> JSONValue {
        .object([
            "source": .string(snapshot.source),
            "snapshotId": .string(snapshot.snapshotId),
            "revision": .integer(snapshot.revision),
            "nodeId": .string(nodeId),
        ])
    }

    private static func collectNodeIds(_ value: JSONValue?) -> Set<String> {
        guard case .object(let node)? = value else { return [] }
        var result = Set<String>()
        if case .string(let nodeId)? = node["id"] { result.insert(nodeId) }
        if case .array(let children)? = node["children"] {
            children.forEach { result.formUnion(collectNodeIds($0)) }
        }
        return result
    }

    private static func stale(
        snapshotId: String,
        source: String,
        message: String,
        latestRevision: Int64? = nil,
        nodeId: String? = nil
    ) -> AnsightToolExecutionResult {
        .failure(
            message,
            errorCode: "stale_node_reference",
            result: .object([
                "source": .string(source),
                "snapshotId": .string(snapshotId),
                "nodeId": nodeId.map(JSONValue.string) ?? .null,
                "latestRevision": latestRevision.map(JSONValue.integer) ?? .null,
                "refreshWith": .string(AnsightVisualTreeToolIds.queryNodes),
            ])
        )
    }
}

private extension NSLock {
    func withLock<T>(_ body: () -> T) -> T {
        lock()
        defer { unlock() }
        return body()
    }
}
