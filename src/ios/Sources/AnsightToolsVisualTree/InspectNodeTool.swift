import AnsightKit
import Foundation

public final class InspectNodeTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.inspectNode,
            name: "Inspect Node",
            description: "Returns detailed metadata for a visual tree node.",
            category: "ui",
            scope: AnsightToolScope.read.rawValue,
            keywords: "ui node inspect accessibility layout",
            security: AnsightVisualTreeToolSecurityProfiles.inspectNode,
            argumentsSchema: AnsightVisualTreeToolSchemas.inspectNodeArguments,
            resultSchema: AnsightVisualTreeToolSchemas.inspectNodeResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeSupport.inspectNode(arguments: arguments)
    }
}
