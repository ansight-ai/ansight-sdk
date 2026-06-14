import AnsightKit
import Foundation

public extension AnsightRuntime {
    func registerPreferencesTools(options: AnsightPreferencesToolOptions = .default) throws {
        for tool in AnsightPreferencesTools.tools(options: options) {
            try registerTool(tool)
        }
    }
}
