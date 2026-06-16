import AnsightCore
import Foundation

public enum AnsightSecureStorageTools {
    public static func tools(options: AnsightSecureStorageToolsOptions = .default) -> [any AnsightTool] {
        [
            GetSecureStorageValueTool(options: options),
            SetSecureStorageValueTool(options: options),
            RemoveSecureStorageKeyTool(options: options),
        ]
    }
}
