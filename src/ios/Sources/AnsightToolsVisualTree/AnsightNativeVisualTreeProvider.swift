import AnsightCore
import Foundation

public final class AnsightNativeVisualTreeProvider: AnsightVisualTreeProvider, AnsightVisualTreeInteractionProvider {
    public let source = AnsightVisualTreeProviderRegistry.nativeSource
    public let displayName = "Native"

    public init() {}

    public func getVisualTree(arguments: [String: String]) -> AnsightToolExecutionResult {
        AnsightVisualTreeSupport.getNativeVisualTree(arguments: arguments)
    }

    public func inspectNode(arguments: [String: String]) -> AnsightToolExecutionResult {
        AnsightVisualTreeSupport.inspectNativeNode(arguments: arguments)
    }

    public func performAction(_ request: AnsightVisualTreeActionRequest) -> AnsightToolExecutionResult {
        AnsightVisualTreeSupport.performNativeAction(request)
    }
}
