import AnsightKit
import Foundation

public final class RemovePreferenceKeyTool: AnsightTool {
    private let options: AnsightPreferencesToolOptions

    public init(options: AnsightPreferencesToolOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightPreferencesToolIds.removeKey,
            name: "Remove Preference Key",
            description: "Deletes a key from an iOS UserDefaults store.",
            category: "prefs",
            scope: AnsightToolScope.delete.rawValue,
            keywords: "preferences userdefaults delete remove settings",
            security: AnsightPreferencesToolSecurityProfiles.removeKey,
            argumentsSchema: AnsightPreferencesToolSchemas.removeKeyArguments,
            resultSchema: AnsightPreferencesToolSchemas.removeKeyResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightPreferencesSupport.removeKey(options: options, arguments: arguments))
        } catch {
            return .failure(error.localizedDescription, errorCode: "prefs_remove_failed")
        }
    }
}
