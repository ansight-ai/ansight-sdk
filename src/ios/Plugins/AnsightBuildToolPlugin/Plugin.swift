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

        return [
            .buildCommand(
                displayName: "Generating Ansight developer build artifacts",
                executable: try context.tool(named: "AnsightBuildTool").url,
                arguments: [
                    "--output-file", outputFile.path,
                    "--target-directory", target.directory.string,
                    "--package-directory", context.package.directoryURL.path,
                ],
                inputFiles: inputFiles,
                outputFiles: [outputFile]
            ),
        ]
    }
}
