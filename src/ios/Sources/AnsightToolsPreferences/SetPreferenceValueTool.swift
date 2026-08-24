import AnsightCore
import Foundation

public final class SetPreferenceValueTool: AnsightTool {
    private let options: AnsightPreferencesToolOptions

    public init(options: AnsightPreferencesToolOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightPreferencesToolIds.setValue,
            name: "Set Preference Value",
            description: "Writes a value to an iOS UserDefaults store.",
            category: "prefs",
            policy: .write,
            keywords: "preferences userdefaults write settings",
            argumentsSchema: AnsightPreferencesToolSchemas.setValueArguments,
            resultSchema: AnsightPreferencesToolSchemas.setValueResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightPreferencesSupport.setValue(options: options, arguments: arguments))
        } catch {
            return .failure(error.localizedDescription, errorCode: "prefs_set_failed")
        }
    }
}
