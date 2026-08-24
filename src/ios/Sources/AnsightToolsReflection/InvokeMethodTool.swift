import AnsightCore
import Foundation

public final class InvokeMethodTool: AnsightTool {
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
            id: AnsightReflectionToolIds.invokeMethod,
            name: "Invoke Method",
            description: "Invokes an opt-in method reachable from a registered iOS object root.",
            category: "reflect",
            policy: .critical,
            keywords: "reflection invoke method runtime",
            argumentsSchema: AnsightReflectionToolSchemas.invokeMethodArguments,
            resultSchema: AnsightReflectionToolSchemas.invokeMethodResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightReflectionSupport.invokeMethod(runtime: runtime, options: options, arguments: arguments))
        } catch {
            return .failure(error.localizedDescription, errorCode: "reflect_invoke_failed")
        }
    }
}
