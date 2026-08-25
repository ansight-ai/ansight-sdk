import AnsightCore
import Foundation

public final class InspectNodeTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.inspectNode,
            name: "Inspect Node",
            description: "Returns detailed metadata for a visual tree node.",
            category: "ui",
            policy: .read,
            keywords: "ui node inspect accessibility layout",
            argumentsSchema: AnsightVisualTreeToolSchemas.inspectNodeArguments,
            resultSchema: AnsightVisualTreeToolSchemas.inspectNodeResult,
            prerequisiteToolIds: [AnsightVisualTreeToolIds.queryNodes]
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        let reference = Self.reference(arguments["reference"])
        guard let nodeId = (arguments["nodeId"] ?? reference?["nodeId"]?.stringValue)?
              .trimmingCharacters(in: .whitespacesAndNewlines),
              !nodeId.isEmpty else {
            return .failure("Node id is required.", errorCode: "node_id_required")
        }
        let source = arguments["source"] ?? reference?["source"]?.stringValue
        var providerArguments = arguments
        providerArguments["nodeId"] = nodeId
        providerArguments["source"] = source
        let snapshot: AnsightVisualTreeSnapshot
        if let snapshotId = arguments["snapshotId"] ?? reference?["snapshotId"]?.stringValue,
           !snapshotId.isEmpty {
            switch AnsightVisualTreeSnapshotStore.validateNode(snapshotId: snapshotId, source: source, nodeId: nodeId) {
            case .failure(let error): return error
            case .success(let value): snapshot = value
            }
        } else {
            let capture = AnsightVisualTreeSnapshotStore.capture(source: source, arguments: providerArguments)
            guard capture.success,
                  case .object(let payload)? = capture.result,
                  case .string(let snapshotId)? = payload["snapshotId"] else {
                return capture
            }
            switch AnsightVisualTreeSnapshotStore.validateNode(snapshotId: snapshotId, source: source, nodeId: nodeId) {
            case .failure(let error): return error
            case .success(let value): snapshot = value
            }
        }

        providerArguments["source"] = snapshot.source
        providerArguments["snapshotId"] = snapshot.snapshotId
        let result = AnsightVisualTreeSupport.inspectNode(arguments: providerArguments)
        guard result.success, case .object(var payload)? = result.result else {
            if result.errorCode.map({ ["visual_tree_node_not_found", "node_not_found", "dom_node_not_found"].contains($0) }) == true {
                return .failure(
                    "Node '\(nodeId)' is no longer valid for snapshot '\(snapshot.snapshotId)'.",
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
        payload["source"] = .string(snapshot.source)
        payload["snapshotId"] = .string(snapshot.snapshotId)
        payload["revision"] = .integer(snapshot.revision)
        payload["reference"] = AnsightVisualTreeSnapshotStore.reference(snapshot: snapshot, nodeId: nodeId)
        return .success(.object(payload), message: result.message)
    }

    private static func reference(_ json: String?) -> [String: JSONValue]? {
        guard let json,
              let data = json.data(using: .utf8),
              let value = try? JSONDecoder().decode(JSONValue.self, from: data),
              case .object(let reference) = value else {
            return nil
        }
        return reference
    }
}
