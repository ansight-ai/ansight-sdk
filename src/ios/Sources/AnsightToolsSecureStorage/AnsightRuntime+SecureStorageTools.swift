import AnsightKit
import Foundation

public extension AnsightRuntime {
    func registerSecureStorageTools(options: AnsightSecureStorageToolsOptions = .default) throws {
        for tool in AnsightSecureStorageTools.tools(options: options) {
            try registerTool(tool)
        }
    }
}
