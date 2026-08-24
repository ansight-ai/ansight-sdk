import AnsightCore
import Foundation

public final class RemoveOverlayTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.removeOverlay,
            name: "Remove Overlay",
            description: "Removes a diagnostic overlay from the active app window by id.",
            category: "ui",
            policy: .write,
            keywords: "ui overlay highlight remove clear",
            argumentsSchema: AnsightVisualTreeToolSchemas.removeOverlayArguments,
            resultSchema: AnsightVisualTreeToolSchemas.removeOverlayResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeOverlaySupport.removeOverlay(arguments: arguments)
    }
}
