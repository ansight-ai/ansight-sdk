import AnsightCore
import Foundation

public final class DescribeTypeTool: AnsightTool {
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
            id: AnsightReflectionToolIds.describeType,
            name: "Describe Type",
            description: "Returns metadata about a runtime type without reading additional live object values.",
            category: "reflect",
            policy: .read,
            keywords: "reflection type members methods metadata runtime",
            argumentsSchema: AnsightReflectionToolSchemas.describeTypeArguments,
            resultSchema: AnsightReflectionToolSchemas.describeTypeResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightReflectionSupport.describeType(runtime: runtime, options: options, arguments: arguments))
        } catch {
            return .failure(error.localizedDescription, errorCode: "reflect_describe_type_failed")
        }
    }
}
