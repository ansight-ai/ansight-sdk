import AnsightCore
import Foundation

public final class QueryNodesTool: AnsightJSONTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.queryNodes,
            name: "Query UI Nodes",
            description: "Captures or reuses a UI snapshot and returns framework-neutral node references.",
            category: "ui",
            policy: .read,
            keywords: "ui query find node selector automation id role text type",
            argumentsSchema: AnsightVisualTreeToolSchemas.queryNodesArguments,
            resultSchema: AnsightVisualTreeToolSchemas.queryNodesResult
        )
    }

    public func execute(arguments: [String: JSONValue]) throws -> AnsightToolExecutionResult {
        AnsightGenericUIQuery.execute(arguments: arguments)
    }
}

public final class PerformActionTool: AnsightJSONTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.performAction,
            name: "Perform UI Action",
            description: "Performs a generic action against a current snapshot-scoped UI node.",
            category: "ui",
            policy: .write,
            keywords: "ui action tap focus set value toggle select node snapshot",
            argumentsSchema: AnsightVisualTreeToolSchemas.performActionArguments,
            resultSchema: AnsightVisualTreeToolSchemas.performActionResult
        )
    }

    public func execute(arguments: [String: JSONValue]) throws -> AnsightToolExecutionResult {
        let reference: [String: JSONValue]
        if case .object(let value)? = arguments["reference"] { reference = value } else { reference = [:] }
        let snapshotId = arguments.string("snapshotId") ?? reference.string("snapshotId")
        let nodeId = arguments.string("nodeId") ?? reference.string("nodeId")
        guard let snapshotId, let nodeId else {
            return .failure(
                "A reference, or both snapshotId and nodeId, is required.",
                errorCode: "ui_action_reference_required"
            )
        }
        guard case .string(let action)? = arguments["action"] else {
            return .failure("action is required.", errorCode: "ui_action_arguments_invalid")
        }
        let source = arguments.string("source") ?? reference.string("source")
        let snapshot: AnsightVisualTreeSnapshot
        switch AnsightVisualTreeSnapshotStore.validateNode(snapshotId: snapshotId, source: source, nodeId: nodeId) {
        case .failure(let error): return error
        case .success(let value): snapshot = value
        }
        guard let provider = AnsightVisualTreeProviderRegistry.provider(for: snapshot.source)
                as? any AnsightVisualTreeInteractionProvider else {
            return .failure(
                "Visual-tree source '\(snapshot.source)' does not support generic UI actions.",
                errorCode: "ui_action_not_supported"
            )
        }
        let value = arguments["value"] ?? arguments["index"] ?? arguments["checked"]
        let options: [String: JSONValue]
        if case .object(let provided)? = arguments["options"] { options = provided } else { options = [:] }
        let result = provider.performAction(.init(nodeId: nodeId, action: action, value: value, options: options))
        guard result.success else {
            if ["visual_tree_node_not_found", "node_not_found", "dom_node_not_found"].contains(where: { $0 == result.errorCode }) {
                return .failure(
                    "Node '\(nodeId)' is no longer valid for snapshot '\(snapshotId)'. Refresh the query and retry.",
                    errorCode: "stale_node_reference",
                    result: .object([
                        "reference": AnsightVisualTreeSnapshotStore.reference(snapshot: snapshot, nodeId: nodeId),
                        "providerError": result.errorCode.map(JSONValue.string) ?? .null,
                        "refreshWith": .string(AnsightVisualTreeToolIds.queryNodes),
                    ])
                )
            }
            return result
        }
        var payload: [String: JSONValue]
        if case .object(let providerPayload)? = result.result { payload = providerPayload } else { payload = [:] }
        payload["source"] = .string(snapshot.source)
        payload["action"] = .string(action)
        payload["reference"] = AnsightVisualTreeSnapshotStore.reference(snapshot: snapshot, nodeId: nodeId)
        return .success(.object(payload), message: result.message)
    }
}

public final class WaitForUIConditionTool: AnsightJSONTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.wait,
            name: "Wait For UI",
            description: "Polls generic UI snapshots until a node condition is met.",
            category: "ui",
            policy: .read,
            keywords: "ui wait poll condition exists visible enabled gone",
            argumentsSchema: AnsightVisualTreeToolSchemas.waitArguments,
            resultSchema: AnsightVisualTreeToolSchemas.waitResult
        )
    }

    public func execute(arguments: [String: JSONValue]) throws -> AnsightToolExecutionResult {
        guard case .string(let condition)? = arguments["condition"] else {
            return .failure("condition is required.", errorCode: "ui_wait_condition_required")
        }
        let timeout = min(max(arguments.integer("timeoutMilliseconds") ?? 5_000, 1), 60_000)
        let poll = min(max(arguments.integer("pollMilliseconds") ?? 100, 10), 5_000)
        let started = Date()
        var lastQuery: JSONValue = .null
        while Date().timeIntervalSince(started) * 1_000 <= Double(timeout) {
            var queryArguments = arguments
            queryArguments.removeValue(forKey: "condition")
            queryArguments.removeValue(forKey: "timeoutMilliseconds")
            queryArguments.removeValue(forKey: "pollMilliseconds")
            queryArguments.removeValue(forKey: "snapshotId")
            if condition == "visible" { queryArguments["visible"] = .bool(true) }
            if condition == "enabled" { queryArguments["enabled"] = .bool(true) }
            let query = AnsightGenericUIQuery.execute(arguments: queryArguments)
            guard query.success, case .object(let payload)? = query.result else { return query }
            lastQuery = .object(payload)
            let count: Int64
            if case .integer(let value)? = payload["count"] { count = value } else { count = 0 }
            let matched = condition == "notExists" ? count == 0 : count > 0
            if matched {
                return .success(.object([
                    "condition": .string(condition),
                    "matched": .bool(true),
                    "elapsedMilliseconds": .integer(Int64(Date().timeIntervalSince(started) * 1_000)),
                    "query": lastQuery,
                ]))
            }
            Thread.sleep(forTimeInterval: Double(poll) / 1_000)
        }
        return .failure(
            "Timed out after \(timeout)ms waiting for UI condition '\(condition)'.",
            errorCode: "ui_wait_timeout",
            result: .object([
                "condition": .string(condition),
                "matched": .bool(false),
                "elapsedMilliseconds": .integer(Int64(Date().timeIntervalSince(started) * 1_000)),
                "lastQuery": lastQuery,
            ])
        )
    }
}

private enum AnsightGenericUIQuery {
    static func execute(arguments: [String: JSONValue]) -> AnsightToolExecutionResult {
        let source = arguments.string("source")
        let snapshot: AnsightVisualTreeSnapshot
        if let snapshotId = arguments.string("snapshotId") {
            switch AnsightVisualTreeSnapshotStore.current(snapshotId: snapshotId, source: source) {
            case .failure(let error): return error
            case .success(let value): snapshot = value
            }
        } else {
            let capture = AnsightVisualTreeSnapshotStore.capture(
                source: source,
                arguments: arguments.compactMapValues(\.stringValue)
            )
            guard capture.success,
                  case .object(let payload)? = capture.result,
                  case .string(let snapshotId)? = payload["snapshotId"] else { return capture }
            switch AnsightVisualTreeSnapshotStore.current(snapshotId: snapshotId, source: source) {
            case .failure(let error): return error
            case .success(let value): snapshot = value
            }
        }

        let maxResults = Int(min(max(arguments.integer("maxResults") ?? 50, 1), 500))
        var matchedNodes: [JSONValue] = []
        var totalMatches: Int64 = 0
        if let root = snapshot.payload["root"] {
            for node in enumerate(root) where matches(payload: snapshot.payload, node: node, arguments: arguments) {
                totalMatches += 1
                if matchedNodes.count < maxResults {
                    var match = node
                    guard case .string(let nodeId)? = node["id"] else { continue }
                    let type = resolveType(payload: snapshot.payload, node: node)
                    match["reference"] = AnsightVisualTreeSnapshotStore.reference(snapshot: snapshot, nodeId: nodeId)
                    match["type"] = type.map(JSONValue.string) ?? .null
                    match["visible"] = .bool(readState(payload: snapshot.payload, node: node, name: "visible", fallbackBit: 1))
                    match["enabled"] = .bool(readState(payload: snapshot.payload, node: node, name: "enabled", fallbackBit: 2))
                    if match["supportedActions"] == nil { match["supportedActions"] = .array(inferActions(type).map(JSONValue.string)) }
                    matchedNodes.append(.object(match))
                }
            }
        }
        return .success(.object([
            "source": .string(snapshot.source),
            "snapshotId": .string(snapshot.snapshotId),
            "revision": .integer(snapshot.revision),
            "count": .integer(Int64(matchedNodes.count)),
            "totalMatches": .integer(totalMatches),
            "truncated": .bool(totalMatches > Int64(matchedNodes.count)),
            "matches": .array(matchedNodes),
        ]))
    }

    private static func enumerate(_ value: JSONValue) -> [[String: JSONValue]] {
        guard case .object(let node) = value else { return [] }
        var result = [node]
        if case .array(let children)? = node["children"] {
            children.forEach { result.append(contentsOf: enumerate($0)) }
        }
        return result
    }

    private static func matches(
        payload: [String: JSONValue],
        node: [String: JSONValue],
        arguments: [String: JSONValue]
    ) -> Bool {
        guard case .string(let nodeId)? = node["id"] else { return false }
        guard equals(nodeId, filter: arguments.string("nodeId")),
              equals(node.string("automationId"), filter: arguments.string("automationId")),
              equals(node.string("role"), filter: arguments.string("role")),
              contains(resolveType(payload: payload, node: node), filter: arguments.string("type")),
              contains(searchText(node), filter: arguments.string("textContains")) else { return false }
        if case .bool(let visible)? = arguments["visible"],
           readState(payload: payload, node: node, name: "visible", fallbackBit: 1) != visible { return false }
        if case .bool(let enabled)? = arguments["enabled"],
           readState(payload: payload, node: node, name: "enabled", fallbackBit: 2) != enabled { return false }
        if let action = arguments.string("action") {
            let actions: [String]
            if case .array(let values)? = node["supportedActions"] {
                actions = values.compactMap { if case .string(let value) = $0 { value } else { nil } }
            } else { actions = inferActions(resolveType(payload: payload, node: node)) }
            if !actions.contains(where: { $0.caseInsensitiveCompare(action) == .orderedSame }) { return false }
        }
        return true
    }

    private static func resolveType(payload: [String: JSONValue], node: [String: JSONValue]) -> String? {
        if let type = node.string("type") { return type }
        guard case .integer(let typeId)? = node["typeId"],
              case .array(let types)? = payload["types"],
              typeId >= 0, typeId < Int64(types.count),
              case .string(let type) = types[Int(typeId)] else { return nil }
        return type
    }

    private static func searchText(_ node: [String: JSONValue]) -> String {
        [node.string("label"), node.string("title"), node.object("visual")?.string("text"), node.object("visual")?.string("value")]
            .compactMap { $0 }
            .joined(separator: " ")
    }

    private static func readState(
        payload: [String: JSONValue],
        node: [String: JSONValue],
        name: String,
        fallbackBit: Int64
    ) -> Bool {
        if case .bool(let state)? = node[name] { return state }
        let bit = payload.object("flagBits")?.integer(name) ?? fallbackBit
        return (node.integer("flags") ?? 0) & bit == bit
    }

    private static func equals(_ value: String?, filter: String?) -> Bool {
        guard let filter else { return true }
        return value?.caseInsensitiveCompare(filter) == .orderedSame
    }

    private static func contains(_ value: String?, filter: String?) -> Bool {
        guard let filter else { return true }
        return value?.localizedCaseInsensitiveContains(filter) == true
    }

    private static func inferActions(_ type: String?) -> [String] {
        let normalized = type?.lowercased() ?? ""
        var actions: [String] = []
        if normalized.contains("button") || normalized.contains("tap") { actions.append("tap") }
        if normalized.contains("entry") || normalized.contains("editor") || normalized.contains("textfield") {
            actions.append(contentsOf: ["focus", "setValue"])
        }
        if normalized.contains("checkbox") || normalized.contains("switch") { actions.append("toggle") }
        if normalized.contains("picker") { actions.append("select") }
        return actions
    }
}

private extension Dictionary where Key == String, Value == JSONValue {
    func string(_ key: String) -> String? {
        if case .string(let value)? = self[key] { return value }
        return nil
    }

    func integer(_ key: String) -> Int64? {
        if case .integer(let value)? = self[key] { return value }
        return nil
    }

    func object(_ key: String) -> [String: JSONValue]? {
        if case .object(let value)? = self[key] { return value }
        return nil
    }
}
