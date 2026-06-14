import Foundation

struct BuildConfiguration {
    let outputFile: URL
    let targetDirectory: URL
    let packageDirectory: URL
    let developerPairingEnabled: Bool
    let developerPairingSourceFile: URL?
    let allowBundledTools: Bool

    init(arguments: [String], environment: [String: String]) throws {
        var outputFile: URL?
        var targetDirectory: URL?
        var packageDirectory: URL?
        var index = 0

        while index < arguments.count {
            let argument = arguments[index]
            switch argument {
            case "--output-file":
                index += 1
                outputFile = URL(fileURLWithPath: try Self.argumentValue(arguments, index: index, name: argument))
            case "--target-directory":
                index += 1
                targetDirectory = URL(fileURLWithPath: try Self.argumentValue(arguments, index: index, name: argument))
            case "--package-directory":
                index += 1
                packageDirectory = URL(fileURLWithPath: try Self.argumentValue(arguments, index: index, name: argument))
            default:
                throw BuildToolError.invalidArguments("Unknown argument '\(argument)'.")
            }

            index += 1
        }

        guard let outputFile, let targetDirectory, let packageDirectory else {
            throw BuildToolError.invalidArguments("Missing required output, target, or package directory argument.")
        }

        let defaultSource = packageDirectory.appendingPathComponent("ansight.json")
        let sourceFile = environment["ANSIGHT_DEVELOPER_PAIRING_SOURCE_FILE"]
            .map(URL.init(fileURLWithPath:))
            ?? (FileManager.default.fileExists(atPath: defaultSource.path) ? defaultSource : nil)

        self.outputFile = outputFile
        self.targetDirectory = targetDirectory
        self.packageDirectory = packageDirectory
        developerPairingEnabled = Self.isEnabled(environment["ANSIGHT_DEVELOPER_PAIRING_ENABLED"])
        developerPairingSourceFile = sourceFile
        allowBundledTools = Self.isEnabled(environment["ANSIGHT_ALLOW_REMOTE_TOOLS"])
    }

    private static func argumentValue(_ arguments: [String], index: Int, name: String) throws -> String {
        guard arguments.indices.contains(index) else {
            throw BuildToolError.invalidArguments("Missing value for '\(name)'.")
        }

        return arguments[index]
    }

    private static func isEnabled(_ rawValue: String?) -> Bool {
        guard let rawValue else {
            return false
        }

        switch rawValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "1", "true", "yes", "on":
            return true
        default:
            return false
        }
    }
}
