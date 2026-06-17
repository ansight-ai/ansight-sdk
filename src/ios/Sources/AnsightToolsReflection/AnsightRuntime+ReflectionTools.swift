import AnsightCore
import Foundation

public extension AnsightRuntime {
    func registerReflectionTools(options: AnsightReflectionToolsOptions = .default) throws {
        for tool in AnsightReflectionTools.tools(options: options, runtime: self) {
            try registerTool(tool)
        }
    }
}
