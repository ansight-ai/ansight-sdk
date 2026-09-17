import Foundation

@objc(AnsightGeneratedBuildArtifactsProvider)
final class AnsightGeneratedBuildArtifactsProvider: NSObject, AnsightBuildArtifactsProviding {
    @objc static func detectedBundledToolTypes() -> [String] {
        []
    }

    @objc static func allowBundledTools() -> Bool {
        switch ProcessInfo.processInfo.environment["ANSIGHT_ALLOW_REMOTE_TOOLS"]?
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .lowercased() {
        case "1", "true", "yes", "on":
            true
        default:
            false
        }
    }
}
