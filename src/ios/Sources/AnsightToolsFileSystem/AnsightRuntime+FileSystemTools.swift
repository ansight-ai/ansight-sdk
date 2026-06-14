import AnsightKit
import Foundation

public extension AnsightRuntime {
    func registerFileSystemTools(options: AnsightFileSystemToolsOptions = .default) throws {
        for tool in AnsightFileSystemTools.tools(options: options) {
            try registerTool(tool)
        }
    }
}
