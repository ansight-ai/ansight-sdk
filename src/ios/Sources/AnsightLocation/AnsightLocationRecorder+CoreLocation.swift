#if canImport(CoreLocation)
import AnsightCore
import CoreLocation
import Foundation

public extension AnsightLocationRecorder {
    func record(
        _ location: CLLocation,
        correlationId: String? = nil,
        runId: String? = nil
    ) async -> OperationResult {
        await record(AnsightLocationSample(
            latitude: location.coordinate.latitude,
            longitude: location.coordinate.longitude,
            altitudeMeters: location.verticalAccuracy >= 0 ? location.altitude : nil,
            horizontalAccuracyMeters: location.horizontalAccuracy >= 0 ? location.horizontalAccuracy : nil,
            verticalAccuracyMeters: location.verticalAccuracy >= 0 ? location.verticalAccuracy : nil,
            speedMetersPerSecond: location.speed >= 0 ? location.speed : nil,
            headingDegrees: location.course >= 0 ? location.course : nil,
            capturedAt: location.timestamp,
            correlationId: correlationId,
            runId: runId
        ))
    }
}
#endif
