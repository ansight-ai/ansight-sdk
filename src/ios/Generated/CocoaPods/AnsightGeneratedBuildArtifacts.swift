import Foundation

@objc(AnsightGeneratedBuildArtifactsProvider)
final class AnsightGeneratedBuildArtifactsProvider: NSObject, AnsightBuildArtifactsProviding {
    @objc static func embeddedDeveloperPairingJsonBase64() -> String? {
        nil
    }

    @objc static func detectedBundledToolTypes() -> [String] {
        []
    }

    @objc static func allowBundledTools() -> Bool {
        false
    }
}