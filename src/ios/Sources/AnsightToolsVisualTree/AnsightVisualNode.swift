import AnsightCore
import Foundation

internal final class AnsightVisualTreeTypeRegistry {
    private var idsByTypeName: [String: Int] = [:]
    private var typeNames: [String] = []

    func typeId(for typeName: String) -> Int {
        let normalizedTypeName = typeName.trimmingCharacters(in: .whitespacesAndNewlines)
        let resolvedTypeName = normalizedTypeName.isEmpty ? "UnknownView" : normalizedTypeName
        if let typeId = idsByTypeName[resolvedTypeName] {
            return typeId
        }

        let typeId = typeNames.count
        typeNames.append(resolvedTypeName)
        idsByTypeName[resolvedTypeName] = typeId
        return typeId
    }

    var jsonValue: JSONValue {
        .array(typeNames.map(JSONValue.string))
    }
}

internal struct AnsightVisualNode: Sendable {
    let id: String
    let type: String
    let automationId: String?
    let label: String?
    let role: String
    let supportedActions: [String]
    let visible: Bool
    let enabled: Bool
    let focusable: Bool
    let bounds: AnsightVisualTreeBounds?
    let visual: [String: JSONValue]
    let z: Double?
    let properties: [String: JSONValue]
    let children: [AnsightVisualNode]

    var nodeCount: Int {
        1 + children.reduce(0) { $0 + $1.nodeCount }
    }

    func find(_ nodeId: String) -> AnsightVisualNode? {
        if id == nodeId {
            return self
        }

        for child in children {
            if let match = child.find(nodeId) {
                return match
            }
        }

        return nil
    }

    func find(_ nodeId: String, ancestors: inout [AnsightVisualNode]) -> AnsightVisualNode? {
        if id == nodeId {
            return self
        }

        for child in children {
            ancestors.append(self)
            if let match = child.find(nodeId, ancestors: &ancestors) {
                return match
            }
            ancestors.removeLast()
        }

        return nil
    }

    func descendants() -> [AnsightVisualNode] {
        children.flatMap { [$0] + $0.descendants() }
    }

    func jsonValue(
        includeBounds: Bool,
        includeProperties: Bool,
        maxDepth: Int,
        typeRegistry: AnsightVisualTreeTypeRegistry
    ) -> JSONValue {
        var payload: [String: JSONValue] = [
            "id": .string(id),
            "typeId": .integer(Int64(typeRegistry.typeId(for: type))),
            "automationId": automationId.map(JSONValue.string) ?? .null,
            "label": label.map(JSONValue.string) ?? .null,
            "role": .string(role),
            "supportedActions": .array(supportedActions.map(JSONValue.string)),
            "interactable": .bool(visible && enabled && !supportedActions.isEmpty),
            "visible": .bool(visible),
            "enabled": .bool(enabled),
            "focusable": .bool(focusable),
            "childCount": .integer(Int64(children.count)),
            "visual": .object(visual),
        ]

        if let z {
            payload["z"] = .number(z)
        }

        if includeBounds, let bounds {
            payload["bounds"] = bounds.jsonValue
        }

        if includeProperties, !properties.isEmpty {
            payload["properties"] = .object(properties)
        }

        if maxDepth > 0 {
            payload["children"] = .array(children.map {
                $0.jsonValue(
                    includeBounds: includeBounds,
                    includeProperties: includeProperties,
                    maxDepth: maxDepth - 1,
                    typeRegistry: typeRegistry
                )
            })
        }

        return .object(payload)
    }
}
