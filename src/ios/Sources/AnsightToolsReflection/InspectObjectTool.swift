import AnsightCore
import Foundation

public final class InspectObjectTool: AnsightTool {
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
            id: AnsightReflectionToolIds.inspectObject,
            name: "Inspect Object",
            description: "Inspects a registered iOS object root and returns an expandable snapshot.",
            category: "reflect",
            scope: AnsightToolScope.read.rawValue,
            keywords: "reflection inspect object runtime properties fields methods",
            security: AnsightReflectionToolSecurityProfiles.inspectObject,
            argumentsSchema: AnsightReflectionToolSchemas.inspectObjectArguments,
            resultSchema: AnsightReflectionToolSchemas.inspectObjectResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightReflectionSupport.inspectObject(runtime: runtime, options: options, arguments: arguments))
        } catch {
            return .failure(error.localizedDescription, errorCode: "reflect_inspect_failed")
        }
    }
}
