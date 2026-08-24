import AnsightCore
import Foundation

public final class GetSecureStorageValueTool: AnsightTool {
    private let options: AnsightSecureStorageToolsOptions

    public init(options: AnsightSecureStorageToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightSecureStorageToolIds.getValue,
            name: "Get Secure Storage Value",
            description: "Reads a decrypted value from the configured secure storage backend.",
            category: "secure",
            policy: .critical,
            keywords: "secure storage keychain keystore encrypted get",
            argumentsSchema: AnsightSecureStorageToolSchemas.getValueArguments,
            resultSchema: AnsightSecureStorageToolSchemas.getValueResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightSecureStorageSupport.getValue(options: options, arguments: arguments))
        } catch AnsightSecureStorageToolError.platformUnsupported(let message) {
            return .failure(message, errorCode: "secure_platform_unsupported")
        } catch {
            return .failure(error.localizedDescription, errorCode: "secure_get_failed")
        }
    }
}
