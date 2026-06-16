import AnsightCore
import Foundation

public protocol AnsightVisualTreeProvider: Sendable {
    var source: String { get }
    var displayName: String { get }

    func getVisualTree(arguments: [String: String]) -> AnsightToolExecutionResult
    func inspectNode(arguments: [String: String]) -> AnsightToolExecutionResult
}
