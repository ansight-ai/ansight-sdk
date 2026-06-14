import Foundation

public struct AnsightBundledToolScanReport: Sendable, Codable, Equatable {
    public let detectedToolTypes: [String]
    public let allowBundledTools: Bool

    public init(detectedToolTypes: [String], allowBundledTools: Bool) {
        self.detectedToolTypes = detectedToolTypes
        self.allowBundledTools = allowBundledTools
    }
}
