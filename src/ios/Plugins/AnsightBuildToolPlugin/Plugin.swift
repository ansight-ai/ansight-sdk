import Foundation
import PackagePlugin

@main
struct AnsightBuildToolPlugin: BuildToolPlugin {
    func createBuildCommands(context: PluginContext, target: Target) throws -> [Command] {
        guard let target = target as? SourceModuleTarget else {
            return []
        }

        let outputFile = context.pluginWorkDirectoryURL.appendingPathComponent("AnsightGeneratedBuildArtifacts.swift")
        var inputFiles = target.sourceFiles.map(\.url)
        let defaultPairingConfig = context.package.directoryURL.appendingPathComponent("ansight.json")
        if FileManager.default.fileExists(atPath: defaultPairingConfig.path) {
            inputFiles.append(defaultPairingConfig)
        }
        let environment = Self.forwardedEnvironment()
        if let sourcePath = environment["ANSIGHT_DEVELOPER_PAIRING_SOURCE_FILE"]?.trimmingCharacters(in: .whitespacesAndNewlines),
           !sourcePath.isEmpty {
            let sourceURL = URL(fileURLWithPath: sourcePath)
            if FileManager.default.fileExists(atPath: sourceURL.path) {
                inputFiles.append(sourceURL)
            }
        }

        return [
            .buildCommand(
                displayName: "Generating Ansight developer build artifacts",
                executable: try context.tool(named: "AnsightBuildTool").url,
                arguments: [
                    "--output-file", outputFile.path,
                    "--target-directory", target.directory.string,
                    "--package-directory", context.package.directoryURL.path,
                ],
                environment: environment,
                inputFiles: inputFiles,
                outputFiles: [outputFile]
            ),
        ]
    }

    private static func forwardedEnvironment() -> [String: String] {
        let allowedKeys = [
            "ANSIGHT_ALLOW_REMOTE_TOOLS",
            "ANSIGHT_DEVELOPER_PAIRING_ENABLED",
            "ANSIGHT_DEVELOPER_PAIRING_SOURCE_FILE",
        ]
        let processEnvironment = ProcessInfo.processInfo.environment
        return Dictionary(uniqueKeysWithValues: allowedKeys.compactMap { key in
            processEnvironment[key].map { (key, $0) }
        })
    }
}
