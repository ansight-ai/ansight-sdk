import Foundation

public struct AnsightMetricStream: Sendable {
    public let channel: AnsightChannel
    private let sampler: @Sendable () -> Double?

    public init(channel: AnsightChannel, sampler: @escaping @Sendable () -> Int64?) {
        self.channel = channel
        self.sampler = { sampler().map(Double.init) }
    }

    public static func floating(channel: AnsightChannel, sampler: @escaping @Sendable () -> Double?) -> AnsightMetricStream {
        AnsightMetricStream(channel: channel, doubleSampler: sampler)
    }

    private init(channel: AnsightChannel, doubleSampler: @escaping @Sendable () -> Double?) {
        self.channel = channel
        self.sampler = doubleSampler
    }

    public func sample() -> Double? {
        sampler()
    }
}
