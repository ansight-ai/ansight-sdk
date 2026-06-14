import AnsightKit
import Foundation

public extension AnsightRuntime {
    func registerDatabaseTools(options: AnsightDatabaseToolsOptions = .default) throws {
        for tool in AnsightDatabaseTools.tools(options: options) {
            try registerTool(tool)
        }
    }
}
