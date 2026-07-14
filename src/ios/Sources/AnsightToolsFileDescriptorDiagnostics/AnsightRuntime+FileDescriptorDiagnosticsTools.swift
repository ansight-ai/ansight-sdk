import AnsightCore
import Foundation

public extension AnsightRuntime {
    func registerFileDescriptorDiagnosticsTools(
        options: AnsightFileDescriptorDiagnosticsOptions = .default
    ) throws {
        for tool in AnsightFileDescriptorDiagnosticsTools.tools(options: options) {
            try registerTool(tool)
        }
    }
}
