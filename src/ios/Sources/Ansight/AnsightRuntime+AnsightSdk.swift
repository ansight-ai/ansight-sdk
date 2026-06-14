import AnsightKit
import Foundation

public extension AnsightRuntime {
    func registerAnsightRemoteTools(options: AnsightRemoteToolOptions = .default) throws {
        for tool in AnsightRemoteTools.tools(options: options, runtime: self) {
            try registerTool(tool)
        }
    }

    func initializeAndActivateAnsightSdk(
        options: AnsightOptions = .ansightDeveloperDefaults,
        remoteToolOptions: AnsightRemoteToolOptions? = .default
    ) throws {
        try initializeAndActivate(options: options)
        if let remoteToolOptions {
            try registerAnsightRemoteTools(options: remoteToolOptions)
        }
    }
}
