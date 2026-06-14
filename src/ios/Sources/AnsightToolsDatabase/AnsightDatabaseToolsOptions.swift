import Foundation

public struct AnsightDatabaseToolsOptions: Sendable, Codable, Equatable {
    public static let `default` = AnsightDatabaseToolsOptions()

    public let additionalRoots: [AnsightDatabaseRoot]
    public let includePlatformRoots: Bool

    public init(
        additionalRoots: [AnsightDatabaseRoot] = [],
        includePlatformRoots: Bool = true
    ) {
        self.additionalRoots = additionalRoots
        self.includePlatformRoots = includePlatformRoots
    }

    public static func createBuilder() -> AnsightDatabaseToolsOptionsBuilder {
        AnsightDatabaseToolsOptionsBuilder()
    }
}
