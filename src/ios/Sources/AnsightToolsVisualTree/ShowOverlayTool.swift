import AnsightCore
import Foundation

public final class ShowOverlayTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.showOverlay,
            name: "Show Overlay",
            description: "Draws an input-transparent diagnostic overlay over the active app window.",
            category: "ui",
            policy: .write,
            keywords: "ui overlay highlight box rectangle diagnostic",
            argumentsSchema: AnsightVisualTreeToolSchemas.showOverlayArguments,
            resultSchema: AnsightVisualTreeToolSchemas.overlayResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeOverlaySupport.showOverlay(arguments: arguments)
    }
}
