import AnsightCore
import Foundation

public enum AnsightDatabaseTools {
    public static func tools(options: AnsightDatabaseToolsOptions = .default) -> [any AnsightTool] {
        [
            ListDatabasesTool(options: options),
            DescribeSchemaTool(options: options),
            QueryDatabaseTool(options: options),
        ]
    }
}
