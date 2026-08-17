import Foundation

public struct AnsightLocationSample: Sendable, Equatable {
    public let latitude: Double
    public let longitude: Double
    public let altitudeMeters: Double?
    public let horizontalAccuracyMeters: Double?
    public let verticalAccuracyMeters: Double?
    public let speedMetersPerSecond: Double?
    public let headingDegrees: Double?
    public let capturedAt: Date
    public let sampleId: String?
    public let correlationId: String?
    public let runId: String?

    public init(
        latitude: Double,
        longitude: Double,
        altitudeMeters: Double? = nil,
        horizontalAccuracyMeters: Double? = nil,
        verticalAccuracyMeters: Double? = nil,
        speedMetersPerSecond: Double? = nil,
        headingDegrees: Double? = nil,
        capturedAt: Date = Date(),
        sampleId: String? = nil,
        correlationId: String? = nil,
        runId: String? = nil
    ) {
        self.latitude = latitude
        self.longitude = longitude
        self.altitudeMeters = altitudeMeters
        self.horizontalAccuracyMeters = horizontalAccuracyMeters
        self.verticalAccuracyMeters = verticalAccuracyMeters
        self.speedMetersPerSecond = speedMetersPerSecond
        self.headingDegrees = headingDegrees
        self.capturedAt = capturedAt
        self.sampleId = sampleId
        self.correlationId = correlationId
        self.runId = runId
    }
}
