import AnsightCore
import Foundation

public actor AnsightLocationRecorder {
    public static let eventType = "CLIENT_LOCATION"
    public static let schema = "ansight.location.sample.v1"

    private let runtime: AnsightRuntime
    private let options: AnsightLocationOptions
    private var lastEmittedSample: AnsightLocationSample?

    public init(
        runtime: AnsightRuntime = .shared,
        options: AnsightLocationOptions = .init()
    ) {
        self.runtime = runtime
        self.options = options
    }

    public func record(_ sample: AnsightLocationSample) async -> OperationResult {
        guard options.enabled else {
            return .failure("Observed location capture is disabled.")
        }
        guard sample.latitude.isFinite, (-90 ... 90).contains(sample.latitude),
              sample.longitude.isFinite, (-180 ... 180).contains(sample.longitude) else {
            return .failure("Observed location coordinates are invalid.")
        }

        let normalized = normalize(sample)
        if shouldSuppress(normalized) {
            return .success("Observed location suppressed by sampling controls.")
        }
        lastEmittedSample = normalized

        return await runtime.sendSessionEvent(type: Self.eventType, payload: payload(normalized))
    }

    private func normalize(_ sample: AnsightLocationSample) -> AnsightLocationSample {
        AnsightLocationSample(
            latitude: rounded(sample.latitude),
            longitude: rounded(sample.longitude),
            altitudeMeters: finite(sample.altitudeMeters),
            horizontalAccuracyMeters: nonNegative(sample.horizontalAccuracyMeters),
            verticalAccuracyMeters: nonNegative(sample.verticalAccuracyMeters),
            speedMetersPerSecond: nonNegative(sample.speedMetersPerSecond),
            headingDegrees: finite(sample.headingDegrees),
            capturedAt: sample.capturedAt,
            sampleId: normalized(sample.sampleId) ?? UUID().uuidString.lowercased(),
            correlationId: normalized(sample.correlationId),
            runId: normalized(sample.runId)
        )
    }

    private func shouldSuppress(_ sample: AnsightLocationSample) -> Bool {
        guard let previous = lastEmittedSample else { return false }
        return sample.capturedAt.timeIntervalSince(previous.capturedAt) < options.minimumInterval
            || distanceMeters(from: previous, to: sample) < options.minimumDistanceMeters
    }

    private func payload(_ sample: AnsightLocationSample) -> [String: JSONValue] {
        var value: [String: JSONValue] = [
            "schema": .string(Self.schema),
            "sampleId": .string(sample.sampleId ?? UUID().uuidString.lowercased()),
            "capturedAtUtc": .string(Self.timestamp(sample.capturedAt)),
            "source": .string("app_observed"),
            "latitude": .number(sample.latitude),
            "longitude": .number(sample.longitude),
        ]
        add(sample.altitudeMeters, as: "altitudeMeters", to: &value)
        add(sample.horizontalAccuracyMeters, as: "horizontalAccuracyMeters", to: &value)
        add(sample.verticalAccuracyMeters, as: "verticalAccuracyMeters", to: &value)
        add(sample.speedMetersPerSecond, as: "speedMetersPerSecond", to: &value)
        add(sample.headingDegrees, as: "headingDegrees", to: &value)
        add(sample.correlationId, as: "correlationId", to: &value)
        add(sample.runId, as: "runId", to: &value)
        return value
    }

    private func rounded(_ value: Double) -> Double {
        let scale = pow(10, Double(options.decimalPlaces))
        return (value * scale).rounded() / scale
    }

    private func distanceMeters(from: AnsightLocationSample, to: AnsightLocationSample) -> Double {
        let radius = 6_371_000.0
        let latitudeDelta = (to.latitude - from.latitude) * .pi / 180
        let longitudeDelta = (to.longitude - from.longitude) * .pi / 180
        let firstLatitude = from.latitude * .pi / 180
        let secondLatitude = to.latitude * .pi / 180
        let haversine = pow(sin(latitudeDelta / 2), 2)
            + cos(firstLatitude) * cos(secondLatitude) * pow(sin(longitudeDelta / 2), 2)
        return 2 * radius * asin(sqrt(haversine))
    }

    private func finite(_ value: Double?) -> Double? {
        value?.isFinite == true ? value : nil
    }

    private func nonNegative(_ value: Double?) -> Double? {
        value?.isFinite == true && value! >= 0 ? value : nil
    }

    private func normalized(_ value: String?) -> String? {
        let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed?.isEmpty == false ? trimmed : nil
    }

    private func add(_ source: Double?, as key: String, to payload: inout [String: JSONValue]) {
        if let source { payload[key] = .number(source) }
    }

    private func add(_ source: String?, as key: String, to payload: inout [String: JSONValue]) {
        if let source { payload[key] = .string(source) }
    }

    private static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
