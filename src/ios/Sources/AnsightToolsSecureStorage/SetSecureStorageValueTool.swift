import AnsightCore
import Foundation

public final class SetSecureStorageValueTool: AnsightTool {
    private let options: AnsightSecureStorageToolsOptions

    public init(options: AnsightSecureStorageToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightSecureStorageToolIds.setValue,
            name: "Set Secure Storage Value",
            description: "Writes a value into the configured secure storage backend.",
            category: "secure",
            policy: .critical,
            keywords: "secure storage keychain keystore encrypted write",
            argumentsSchema: AnsightSecureStorageToolSchemas.setValueArguments,
            resultSchema: AnsightSecureStorageToolSchemas.setValueResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightSecureStorageSupport.setValue(options: options, arguments: arguments))
        } catch AnsightSecureStorageToolError.platformUnsupported(let message) {
            return .failure(message, errorCode: "secure_platform_unsupported")
        } catch {
            return .failure(error.localizedDescription, errorCode: "secure_set_failed")
        }
    }
}
