import Foundation

@objc
internal protocol AnsightBuildArtifactsProviding {
    static func detectedBundledToolTypes() -> [String]
    static func allowBundledTools() -> Bool
}
