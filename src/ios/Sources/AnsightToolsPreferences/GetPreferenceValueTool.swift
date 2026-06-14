import AnsightKit
import Foundation

public final class GetPreferenceValueTool: AnsightTool {
    private let options: AnsightPreferencesToolOptions

    public init(options: AnsightPreferencesToolOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightPreferencesToolIds.getValue,
            name: "Get Preference Value",
            description: "Reads a value from an iOS UserDefaults store.",
            category: "prefs",
            scope: AnsightToolScope.read.rawValue,
            keywords: "preferences userdefaults read settings",
            security: AnsightPreferencesToolSecurityProfiles.getValue,
            argumentsSchema: AnsightPreferencesToolSchemas.getValueArguments,
            resultSchema: AnsightPreferencesToolSchemas.getValueResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            return .success(try AnsightPreferencesSupport.getValue(options: options, arguments: arguments))
        } catch {
            return .failure(error.localizedDescription, errorCode: "prefs_get_failed")
        }
    }
}
