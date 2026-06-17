import AnsightCore
import Foundation

public final class SetMemberValueTool: AnsightTool {
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
            id: AnsightReflectionToolIds.setMemberValue,
            name: "Set Member Value",
            description: "Writes an opt-in member reachable from a registered iOS object root.",
            category: "reflect",
            scope: AnsightToolScope.write.rawValue,
            keywords: "reflection set write property field runtime",
            security: AnsightReflectionToolSecurityProfiles.setMemberValue,
            argumentsSchema: AnsightReflectionToolSchemas.setMemberValueArguments,
            resultSchema: AnsightReflectionToolSchemas.setMemberValueResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightReflectionSupport.setMemberValue(runtime: runtime, options: options, arguments: arguments))
        } catch {
            return .failure(error.localizedDescription, errorCode: "reflect_set_member_failed")
        }
    }
}
