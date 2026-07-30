import Foundation

@main
struct AnsightBuildToolMain {
    static func main() throws {
        let configuration = try BuildConfiguration(
            arguments: Array(CommandLine.arguments.dropFirst()),
            environment: ProcessInfo.processInfo.environment
        )

        let artifacts = try buildArtifacts(configuration: configuration)
        try writeArtifacts(artifacts, to: configuration.outputFile)

        if !artifacts.detectedToolTypes.isEmpty, !artifacts.allowBundledTools {
            let summary = artifacts.detectedToolTypes.joined(separator: ", ")
            fputs(
                """
                error: Ansight detected concrete AnsightTool implementations in this target: \(summary). \
                Set ANSIGHT_ALLOW_REMOTE_TOOLS=true only for local developer builds.
                """,
                stderr
            )
            Foundation.exit(1)
        }
    }

    private static func buildArtifacts(configuration: BuildConfiguration) throws -> BuildArtifacts {
        return BuildArtifacts(
            detectedToolTypes: detectBundledTools(in: configuration.targetDirectory),
            allowBundledTools: configuration.allowBundledTools
        )
    }

    private static func detectBundledTools(in targetDirectory: URL) -> [String] {
        guard let enumerator = FileManager.default.enumerator(
            at: targetDirectory,
            includingPropertiesForKeys: nil
        ) else {
            return []
        }

        let pattern = #"(?m)\b(?:public\s+|internal\s+|fileprivate\s+|private\s+)?(?:final\s+)?(?:class|struct|actor|enum)\s+([A-Za-z_][A-Za-z0-9_]*)[^{\n]*\bAnsightTool\b"#
        let regex = try? NSRegularExpression(pattern: pattern)
        var detected = Set<String>()

        for case let fileURL as URL in enumerator where fileURL.pathExtension == "swift" {
            guard let contents = try? String(contentsOf: fileURL, encoding: .utf8),
                  let regex else {
                continue
            }

            let range = NSRange(contents.startIndex..<contents.endIndex, in: contents)
            for match in regex.matches(in: contents, options: [], range: range) {
                guard let nameRange = Range(match.range(at: 1), in: contents) else {
                    continue
                }

                detected.insert(String(contents[nameRange]))
            }
        }

        return detected.sorted()
    }

    private static func writeArtifacts(_ artifacts: BuildArtifacts, to outputFile: URL) throws {
        try FileManager.default.createDirectory(
            at: outputFile.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        let source = renderedSwiftSource(for: artifacts)

        do {
            try source.write(to: outputFile, atomically: true, encoding: .utf8)
        } catch {
            throw BuildToolError.writeFailed("Failed to write generated artifacts: \(error.localizedDescription)")
        }
    }

    private static func renderedSwiftSource(for artifacts: BuildArtifacts) -> String {
        let toolLiterals = artifacts.detectedToolTypes.map { "\"\($0.replacingOccurrences(of: "\"", with: "\\\""))\"" }
            .joined(separator: ", ")

        return """
        import Foundation

        @objc(AnsightGeneratedBuildArtifactsProvider)
        final class AnsightGeneratedBuildArtifactsProvider: NSObject, AnsightBuildArtifactsProviding {
            @objc static func detectedBundledToolTypes() -> [String] {
                [\(toolLiterals)]
            }

            @objc static func allowBundledTools() -> Bool {
                \(artifacts.allowBundledTools ? "true" : "false")
            }
        }
        """
    }
}
