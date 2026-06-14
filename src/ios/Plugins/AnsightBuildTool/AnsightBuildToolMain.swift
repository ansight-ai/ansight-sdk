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
        let embeddedDeveloperPairingJsonBase64 = try createDeveloperPairing(
            enabled: configuration.developerPairingEnabled,
            sourceFile: configuration.developerPairingSourceFile
        )?.data(using: .utf8)?.base64EncodedString()

        return BuildArtifacts(
            embeddedDeveloperPairingJsonBase64: embeddedDeveloperPairingJsonBase64,
            detectedToolTypes: detectBundledTools(in: configuration.targetDirectory),
            allowBundledTools: configuration.allowBundledTools
        )
    }

    private static func createDeveloperPairing(enabled: Bool, sourceFile: URL?) throws -> String? {
        guard enabled, let sourceFile else {
            return nil
        }

        guard FileManager.default.fileExists(atPath: sourceFile.path) else {
            return nil
        }

        let pairingConfigData = try Data(contentsOf: sourceFile)
        let pairingConfigObject = try JSONSerialization.jsonObject(with: pairingConfigData, options: [])
        let discoveryHint = makeDiscoveryHint()

        let document: [String: Any] = [
            "schema": "ansight.pairing-ticket.v1",
            "config": pairingConfigObject,
            "discovery": discoveryHint,
        ]

        let json = try JSONSerialization.data(withJSONObject: document, options: [.sortedKeys])
        return String(data: json, encoding: .utf8)
    }

    private static func makeDiscoveryHint() -> [String: Any] {
        let hostAddresses = preferredHostAddresses()
        return [
            "schema": "ansight.discovery-hint.v1",
            "source": "developer-pairing-swiftpm",
            "hostAddresses": hostAddresses,
            "hostName": shell("hostname")?.trimmingCharacters(in: .whitespacesAndNewlines) as Any,
            "wifiName": currentWifiName() as Any,
            "capturedAt": makeTimestamp(),
        ]
    }

    private static func preferredHostAddresses() -> [String] {
        let wifiDevice = shell(#"networksetup -listallhardwareports | awk '/Wi-Fi|AirPort/{getline; print $2; exit}'"#)?
            .trimmingCharacters(in: .whitespacesAndNewlines)
        let defaultDevice = shell(#"route -n get default | awk '/interface:/{print $2; exit}'"#)?
            .trimmingCharacters(in: .whitespacesAndNewlines)

        var hostAddresses: [String] = []
        if let defaultDevice, !defaultDevice.isEmpty {
            appendInterfaceAddresses(for: defaultDevice, to: &hostAddresses)
        }

        if let wifiDevice, !wifiDevice.isEmpty, wifiDevice != defaultDevice {
            appendInterfaceAddresses(for: wifiDevice, to: &hostAddresses)
        }

        return hostAddresses
    }

    private static func appendInterfaceAddresses(for device: String, to hostAddresses: inout [String]) {
        guard let rawAddresses = shell("""
            ifconfig \(device) 2>/dev/null | awk '
              /^[[:space:]]*inet / {
                address = $2
                if (address != "127.0.0.1" && address !~ /^169\\.254\\./) {
                  print address
                }
              }
              /^[[:space:]]*inet6 / {
                address = $2
                sub(/%.*/, "", address)
                lower = tolower(address)
                if (lower != "::1" && lower !~ /^fe80:/) {
                  print address
                }
              }
            '
            """) else {
            return
        }

        rawAddresses
            .split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .forEach { address in
                if !hostAddresses.contains(where: { $0.caseInsensitiveCompare(address) == .orderedSame }) {
                    hostAddresses.append(address)
                }
            }
    }

    private static func currentWifiName() -> String? {
        guard let wifiDevice = shell(#"networksetup -listallhardwareports | awk '/Wi-Fi|AirPort/{getline; print $2; exit}'"#)?
            .trimmingCharacters(in: .whitespacesAndNewlines),
            !wifiDevice.isEmpty,
            let rawName = shell("networksetup -getairportnetwork \(wifiDevice)")?
                .replacingOccurrences(of: "Current Wi-Fi Network: ", with: "")
                .trimmingCharacters(in: .whitespacesAndNewlines),
            !rawName.isEmpty,
            rawName != "You are not associated with an AirPort network."
        else {
            return nil
        }

        return rawName
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
        let pairingLiteral = artifacts.embeddedDeveloperPairingJsonBase64.map { "\"\($0)\"" } ?? "nil"
        let toolLiterals = artifacts.detectedToolTypes.map { "\"\($0.replacingOccurrences(of: "\"", with: "\\\""))\"" }
            .joined(separator: ", ")

        return """
        import Foundation

        @objc(AnsightGeneratedBuildArtifactsProvider)
        final class AnsightGeneratedBuildArtifactsProvider: NSObject, AnsightBuildArtifactsProviding {
            @objc static func embeddedDeveloperPairingJsonBase64() -> String? {
                \(pairingLiteral)
            }

            @objc static func detectedBundledToolTypes() -> [String] {
                [\(toolLiterals)]
            }

            @objc static func allowBundledTools() -> Bool {
                \(artifacts.allowBundledTools ? "true" : "false")
            }
        }
        """
    }

    private static func shell(_ command: String) -> String? {
        #if os(macOS)
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/bash")
        process.arguments = ["-lc", command]

        let output = Pipe()
        process.standardOutput = output
        process.standardError = Pipe()

        do {
            try process.run()
            process.waitUntilExit()
            guard process.terminationStatus == 0 else {
                return nil
            }

            let data = output.fileHandleForReading.readDataToEndOfFile()
            return String(data: data, encoding: .utf8)
        } catch {
            return nil
        }
        #else
        return nil
        #endif
    }

    private static func makeTimestamp() -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: Date())
    }
}
