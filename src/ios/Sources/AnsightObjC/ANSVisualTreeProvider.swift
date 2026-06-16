import Ansight
import Foundation

@objc(ANSVisualTreeProvider)
public final class ANSVisualTreeProvider: NSObject, AnsightVisualTreeProvider, @unchecked Sendable {
    @objc public let source: String
    @objc public let displayName: String

    private let getVisualTreeBlock: @Sendable (NSDictionary) -> NSDictionary?
    private let inspectNodeBlock: @Sendable (NSDictionary) -> NSDictionary?

    @objc(initWithSource:displayName:getVisualTree:inspectNode:)
    public init(
        source: String,
        displayName: String,
        getVisualTree: @escaping @Sendable (NSDictionary) -> NSDictionary?,
        inspectNode: @escaping @Sendable (NSDictionary) -> NSDictionary?
    ) {
        self.source = source
        self.displayName = displayName
        self.getVisualTreeBlock = getVisualTree
        self.inspectNodeBlock = inspectNode
    }

    public func getVisualTree(arguments: [String: String]) -> AnsightToolExecutionResult {
        execute(block: getVisualTreeBlock, arguments: arguments, missingMessage: "Visual tree provider returned no tree.")
    }

    public func inspectNode(arguments: [String: String]) -> AnsightToolExecutionResult {
        execute(block: inspectNodeBlock, arguments: arguments, missingMessage: "Visual tree provider returned no node.")
    }

    private func execute(
        block: @Sendable (NSDictionary) -> NSDictionary?,
        arguments: [String: String],
        missingMessage: String
    ) -> AnsightToolExecutionResult {
        guard let dictionary = block(NSDictionary(dictionary: arguments)) else {
            return .failure(missingMessage, errorCode: "visual_tree_provider_empty_result")
        }

        do {
            return .success(try ANSJSONBridge.jsonValue(from: dictionary))
        } catch {
            return .failure(error.localizedDescription, errorCode: "visual_tree_provider_invalid_json")
        }
    }
}
