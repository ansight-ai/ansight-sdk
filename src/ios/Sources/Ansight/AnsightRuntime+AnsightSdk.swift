import AnsightCore
import AnsightPairingQR
import Foundation

public extension AnsightRuntime {
    func registerAnsightRemoteTools(options: AnsightRemoteToolOptions = .default) throws {
        for tool in AnsightRemoteTools.tools(options: options, runtime: self) {
            if isToolRegistered(tool.descriptor.id) {
                continue
            }
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

    func initializeAndActivateAnsightSdk(
        configureOptions: (AnsightOptionsBuilder) -> Void,
        remoteToolOptions: AnsightRemoteToolOptions? = .default,
        hostConnectionConfigReader: (any HostConnectionConfigReading)? = PlatformHostConnectionConfigReader()
    ) throws {
        let builder = AnsightOptions.createBuilder(.ansightDeveloperDefaults)
        configureOptions(builder)
        try initializeAndActivateAnsightSdk(
            options: try builder.build(),
            remoteToolOptions: remoteToolOptions,
            hostConnectionConfigReader: hostConnectionConfigReader
        )
    }
}
