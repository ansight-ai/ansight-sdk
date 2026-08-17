import Foundation

public struct AnsightLocationOptions: Sendable, Equatable {
    public var enabled: Bool
    public var decimalPlaces: Int
    public var minimumInterval: TimeInterval
    public var minimumDistanceMeters: Double

    public init(
        enabled: Bool = false,
        decimalPlaces: Int = 5,
        minimumInterval: TimeInterval = 1,
        minimumDistanceMeters: Double = 1
    ) {
        self.enabled = enabled
        self.decimalPlaces = min(7, max(0, decimalPlaces))
        self.minimumInterval = max(0, minimumInterval)
        self.minimumDistanceMeters = minimumDistanceMeters.isFinite
            ? max(0, minimumDistanceMeters)
            : 0
    }

    public static func enabled(
        decimalPlaces: Int = 5,
        minimumInterval: TimeInterval = 1,
        minimumDistanceMeters: Double = 1
    ) -> AnsightLocationOptions {
        AnsightLocationOptions(
            enabled: true,
            decimalPlaces: decimalPlaces,
            minimumInterval: minimumInterval,
            minimumDistanceMeters: minimumDistanceMeters
        )
    }
}
