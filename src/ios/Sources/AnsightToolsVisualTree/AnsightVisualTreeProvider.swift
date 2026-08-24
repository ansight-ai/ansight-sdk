import AnsightCore
import Foundation

public protocol AnsightVisualTreeProvider: Sendable {
    var source: String { get }
    var displayName: String { get }

    func getVisualTree(arguments: [String: String]) -> AnsightToolExecutionResult
    func inspectNode(arguments: [String: String]) -> AnsightToolExecutionResult
}

public struct AnsightVisualTreeActionRequest: Sendable, Equatable {
    public let nodeId: String
    public let action: String
    public let value: JSONValue?
    public let options: [String: JSONValue]

    public init(nodeId: String, action: String, value: JSONValue?, options: [String: JSONValue]) {
        self.nodeId = nodeId
        self.action = action
        self.value = value
        self.options = options
    }
}

public protocol AnsightVisualTreeInteractionProvider: Sendable {
    func performAction(_ request: AnsightVisualTreeActionRequest) -> AnsightToolExecutionResult
}
