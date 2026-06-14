import AnsightKit
import Foundation

public enum AnsightPreferencesTools {
    public static func tools(options: AnsightPreferencesToolOptions = .default) -> [any AnsightTool] {
        [
            ListPreferenceKeysTool(options: options),
            GetPreferenceValueTool(options: options),
            SetPreferenceValueTool(options: options),
            RemovePreferenceKeyTool(options: options),
        ]
    }
}
