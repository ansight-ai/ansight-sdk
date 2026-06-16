import AnsightCore
import Foundation

public final class GetOverlayTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.getOverlay,
            name: "Get Overlay",
            description: "Returns metadata and geometry for a live diagnostic overlay.",
            category: "ui",
            scope: AnsightToolScope.read.rawValue,
            keywords: "ui overlay highlight inspect metadata",
            security: AnsightVisualTreeToolSecurityProfiles.getOverlay,
            argumentsSchema: AnsightVisualTreeToolSchemas.getOverlayArguments,
            resultSchema: AnsightVisualTreeToolSchemas.overlayResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeOverlaySupport.getOverlay(arguments: arguments)
    }
}
