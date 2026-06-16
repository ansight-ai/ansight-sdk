import Foundation

public struct DefaultMemoryChannels: OptionSet, Sendable, Codable, Hashable {
    public let rawValue: Int

    public static let none = DefaultMemoryChannels([])
    public static let managedHeap = DefaultMemoryChannels(rawValue: 1 << 0)
    public static let nativeHeap = DefaultMemoryChannels(rawValue: 1 << 1)
    public static let residentSetSize = DefaultMemoryChannels(rawValue: 1 << 2)
    public static let physicalFootprint = DefaultMemoryChannels(rawValue: 1 << 3)
    public static let all: DefaultMemoryChannels = [.managedHeap, .nativeHeap, .residentSetSize, .physicalFootprint]
    public static let platformDefaults: DefaultMemoryChannels = [.managedHeap, .physicalFootprint]

    public init(rawValue: Int) {
        self.rawValue = rawValue
    }
}
