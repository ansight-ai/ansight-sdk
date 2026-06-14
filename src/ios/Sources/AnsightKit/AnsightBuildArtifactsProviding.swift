import Foundation

@objc
internal protocol AnsightBuildArtifactsProviding {
    static func embeddedDeveloperPairingJsonBase64() -> String?
    static func detectedBundledToolTypes() -> [String]
    static func allowBundledTools() -> Bool
}
