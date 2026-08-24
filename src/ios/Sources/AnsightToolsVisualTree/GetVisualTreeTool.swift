import AnsightCore
import Foundation

public final class GetVisualTreeTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.getVisualTree,
            name: "Get Visual Tree",
            description: "Returns the current UI hierarchy for the foreground scene.",
            category: "ui",
            policy: .read,
            keywords: "ui visual tree hierarchy layout",
            argumentsSchema: AnsightVisualTreeToolSchemas.getVisualTreeArguments,
            resultSchema: AnsightVisualTreeToolSchemas.visualTreeResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeSnapshotStore.capture(source: arguments["source"], arguments: arguments)
    }
}
