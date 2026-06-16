import Foundation

public struct AnsightMetricStream: Sendable {
    public let channel: AnsightChannel
    private let sampler: @Sendable () -> Int64?

    public init(channel: AnsightChannel, sampler: @escaping @Sendable () -> Int64?) {
        self.channel = channel
        self.sampler = sampler
    }

    public func sample() -> Int64? {
        sampler()
    }
}
