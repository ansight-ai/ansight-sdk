import AnsightCore
import Foundation

public final class UpdateOverlayTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.updateOverlay,
            name: "Update Overlay",
            description: "Edits an existing input-transparent diagnostic overlay.",
            category: "ui",
            policy: .write,
            keywords: "ui overlay highlight update edit mutate",
            argumentsSchema: AnsightVisualTreeToolSchemas.updateOverlayArguments,
            resultSchema: AnsightVisualTreeToolSchemas.overlayResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeOverlaySupport.updateOverlay(arguments: arguments)
    }
}
