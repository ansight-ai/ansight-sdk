import AnsightCore
import Foundation

public final class ListPreferenceKeysTool: AnsightTool {
    private let options: AnsightPreferencesToolOptions

    public init(options: AnsightPreferencesToolOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightPreferencesToolIds.listKeys,
            name: "List Preference Keys",
            description: "Lists visible keys from an iOS UserDefaults store.",
            category: "prefs",
            scope: AnsightToolScope.read.rawValue,
            keywords: "preferences userdefaults keys settings",
            security: AnsightPreferencesToolSecurityProfiles.listKeys,
            argumentsSchema: AnsightPreferencesToolSchemas.listKeysArguments,
            resultSchema: AnsightPreferencesToolSchemas.listKeysResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightPreferencesSupport.listKeys(options: options, arguments: arguments))
        } catch {
            return .failure(error.localizedDescription, errorCode: "prefs_list_failed")
        }
    }
}
