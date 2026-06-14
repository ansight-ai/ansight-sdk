import AnsightKit
import Foundation

public final class RemoveSecureStorageKeyTool: AnsightTool {
    private let options: AnsightSecureStorageToolsOptions

    public init(options: AnsightSecureStorageToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightSecureStorageToolIds.removeKey,
            name: "Remove Secure Storage Key",
            description: "Deletes a value from the configured secure storage backend.",
            category: "secure",
            scope: AnsightToolScope.delete.rawValue,
            keywords: "secure storage keychain keystore encrypted delete remove",
            security: AnsightSecureStorageToolSecurityProfiles.removeKey,
            argumentsSchema: AnsightSecureStorageToolSchemas.removeKeyArguments,
            resultSchema: AnsightSecureStorageToolSchemas.removeKeyResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightSecureStorageSupport.removeKey(options: options, arguments: arguments))
        } catch AnsightSecureStorageToolError.platformUnsupported(let message) {
            return .failure(message, errorCode: "secure_platform_unsupported")
        } catch {
            return .failure(error.localizedDescription, errorCode: "secure_remove_failed")
        }
    }
}
