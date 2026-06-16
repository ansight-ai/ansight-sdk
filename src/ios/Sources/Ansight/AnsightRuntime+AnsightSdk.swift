import AnsightCore
import AnsightPairingQR
import Foundation

public extension AnsightRuntime {
    func registerAnsightRemoteTools(options: AnsightRemoteToolOptions = .default) throws {
        for tool in AnsightRemoteTools.tools(options: options, runtime: self) {
            try registerTool(tool)
        }
    }

    func initializeAndActivateAnsightSdk(
        options: AnsightOptions = .ansightDeveloperDefaults,
        remoteToolOptions: AnsightRemoteToolOptions? = .default,
        hostConnectionConfigReader: (any HostConnectionConfigReading)? = PlatformHostConnectionConfigReader()
    ) throws {
        try initialize(options: options)
        setHostConnectionConfigReader(hostConnectionConfigReader)
        try activate()
        if let remoteToolOptions {
            try registerAnsightRemoteTools(options: remoteToolOptions)
        }
    }
}
