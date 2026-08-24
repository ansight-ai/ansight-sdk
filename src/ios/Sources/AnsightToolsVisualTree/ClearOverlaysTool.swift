import AnsightCore
import Foundation

public final class ClearOverlaysTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.clearOverlays,
            name: "Clear Overlays",
            description: "Removes all diagnostic overlays from the active app window.",
            category: "ui",
            policy: .write,
            keywords: "ui overlay highlight clear remove all",
            argumentsSchema: AnsightVisualTreeToolSchemas.clearOverlaysArguments,
            resultSchema: AnsightVisualTreeToolSchemas.clearOverlaysResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeOverlaySupport.clearOverlays(arguments: arguments)
    }
}
