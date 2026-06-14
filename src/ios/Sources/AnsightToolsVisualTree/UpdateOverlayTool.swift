import AnsightKit
import Foundation

public final class UpdateOverlayTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.updateOverlay,
            name: "Update Overlay",
            description: "Edits an existing input-transparent diagnostic overlay.",
            category: "ui",
            scope: AnsightToolScope.write.rawValue,
            keywords: "ui overlay highlight update edit mutate",
            security: AnsightVisualTreeToolSecurityProfiles.updateOverlay,
            argumentsSchema: AnsightVisualTreeToolSchemas.updateOverlayArguments,
            resultSchema: AnsightVisualTreeToolSchemas.overlayResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeOverlaySupport.updateOverlay(arguments: arguments)
    }
}
