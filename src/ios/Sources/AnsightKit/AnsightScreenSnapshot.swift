import Foundation

public struct AnsightScreenSnapshot: Sendable, Equatable {
    public let width: Int
    public let height: Int
    public let data: Data

    public init(width: Int, height: Int, data: Data) {
        self.width = width
        self.height = height
        self.data = data
    }
}
