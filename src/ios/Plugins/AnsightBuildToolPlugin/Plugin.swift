import Foundation
import PackagePlugin

@main
struct AnsightBuildToolPlugin: BuildToolPlugin {
    func createBuildCommands(context: PluginContext, target: Target) throws -> [Command] {
        guard let target = target as? SourceModuleTarget else {
            return []
        }

        let outputFile = context.pluginWorkDirectoryURL.appendingPathComponent("AnsightGeneratedBuildArtifacts.swift")
        let environment = Self.forwardedEnvironment()

        return [
            .buildCommand(
                displayName: "Generating Ansight developer build artifacts",
                executable: try context.tool(named: "AnsightBuildTool").url,
                arguments: [
                    "--output-file", outputFile.path,
                    "--target-directory", target.directory.string,
                ],
                environment: environment,
                inputFiles: target.sourceFiles.map(\.url),
                outputFiles: [outputFile]
            ),
        ]
    }

    private static func forwardedEnvironment() -> [String: String] {
        let allowedKeys = [
            "ANSIGHT_ALLOW_REMOTE_TOOLS",
        ]
        let processEnvironment = ProcessInfo.processInfo.environment
        return Dictionary(uniqueKeysWithValues: allowedKeys.compactMap { key in
            processEnvironment[key].map { (key, $0) }
        })
    }
}
