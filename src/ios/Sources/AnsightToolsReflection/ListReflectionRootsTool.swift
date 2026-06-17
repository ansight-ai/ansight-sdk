import AnsightCore
import Foundation

public final class ListReflectionRootsTool: AnsightTool {
    private let runtime: AnsightRuntime
    private let options: AnsightReflectionToolsOptions

    public init(
        options: AnsightReflectionToolsOptions = .default,
        runtime: AnsightRuntime = .shared
    ) {
        self.runtime = runtime
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightReflectionToolIds.listRoots,
            name: "List Reflection Roots",
            description: "Lists registered iOS runtime object roots available for reflection tools.",
            category: "reflect",
            scope: AnsightToolScope.read.rawValue,
            keywords: "reflection runtime inspect roots objects",
            security: AnsightReflectionToolSecurityProfiles.listRoots,
            argumentsSchema: AnsightReflectionToolSchemas.listRootsArguments,
            resultSchema: AnsightReflectionToolSchemas.listRootsResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        .success(AnsightReflectionSupport.listRoots(runtime: runtime, options: options))
    }
}
