import Foundation

struct BuildConfiguration {
    let outputFile: URL
    let targetDirectory: URL
    let allowBundledTools: Bool

    init(arguments: [String], environment: [String: String]) throws {
        var outputFile: URL?
        var targetDirectory: URL?
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
            default:
                throw BuildToolError.invalidArguments("Unknown argument '\(argument)'.")
            }

            index += 1
        }

        guard let outputFile, let targetDirectory else {
            throw BuildToolError.invalidArguments("Missing required output or target directory argument.")
        }

        self.outputFile = outputFile
        self.targetDirectory = targetDirectory
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
