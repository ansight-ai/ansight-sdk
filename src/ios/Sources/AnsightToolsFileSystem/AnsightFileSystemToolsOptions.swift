import Foundation

public struct AnsightFileSystemToolsOptions: Sendable, Codable, Equatable {
    public static let `default` = AnsightFileSystemToolsOptions()

    public let additionalRoots: [AnsightFileSystemRoot]

    public init(additionalRoots: [AnsightFileSystemRoot] = []) {
        self.additionalRoots = additionalRoots.filter { !$0.alias.isEmpty && !$0.path.isEmpty }
    }

    public static func createBuilder() -> AnsightFileSystemToolsOptionsBuilder {
        AnsightFileSystemToolsOptionsBuilder()
    }
}
