import AnsightCore
import Foundation

public final class QueryOverlaysTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.queryOverlays,
            name: "Query Overlays",
            description: "Lists live diagnostic overlays and supports simple metadata filtering.",
            category: "ui",
            scope: AnsightToolScope.read.rawValue,
            keywords: "ui overlay highlight query list metadata",
            security: AnsightVisualTreeToolSecurityProfiles.queryOverlays,
            argumentsSchema: AnsightVisualTreeToolSchemas.queryOverlaysArguments,
            resultSchema: AnsightVisualTreeToolSchemas.queryOverlaysResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeOverlaySupport.queryOverlays(arguments: arguments)
    }
}
