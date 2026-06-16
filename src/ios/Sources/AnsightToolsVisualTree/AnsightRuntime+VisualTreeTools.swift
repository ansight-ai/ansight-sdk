import AnsightCore
import Foundation

public extension AnsightRuntime {
    func registerVisualTreeTools() throws {
        for tool in AnsightVisualTreeTools.tools(runtime: self) {
            try registerTool(tool)
        }
    }
}
