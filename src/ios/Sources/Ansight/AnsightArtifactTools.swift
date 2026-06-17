import AnsightCore
import Foundation

public enum AnsightArtifactTools {
    public static func tools(
        providers: [any AnsightArtifactProvider],
        runtime: AnsightRuntime = .shared
    ) -> [any AnsightTool] {
        let capturedProviders = providers
        return tools(providers: { capturedProviders }, runtime: runtime)
    }

    public static func tools(
        providers: @escaping @Sendable () -> [any AnsightArtifactProvider],
        runtime: AnsightRuntime = .shared
    ) -> [any AnsightTool] {
        [
            ClosureAnsightTool(descriptor: AnsightArtifactToolSupport.queryDescriptor) { arguments in
                try AnsightArtifactToolSupport.executeQuery(arguments: arguments, providers: providers)
            },
            ClosureAnsightTool(descriptor: AnsightArtifactToolSupport.requestDescriptor) { [weak runtime] arguments in
                guard let runtime else {
                    return .failure("AnsightRuntime is no longer available.", errorCode: "artifact_request_failed")
                }

                return try AnsightArtifactToolSupport.executeRequest(
                    arguments: arguments,
                    providers: providers,
                    runtime: runtime
                )
            },
        ]
    }
}

private final class ClosureAnsightTool: AnsightTool {
    let descriptor: AnsightToolDescriptor
    private let executeHandler: @Sendable ([String: String]) throws -> AnsightToolExecutionResult

    init(
        descriptor: AnsightToolDescriptor,
        execute: @escaping @Sendable ([String: String]) throws -> AnsightToolExecutionResult
    ) {
        self.descriptor = descriptor
        executeHandler = execute
    }

    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        try executeHandler(arguments)
    }
}
