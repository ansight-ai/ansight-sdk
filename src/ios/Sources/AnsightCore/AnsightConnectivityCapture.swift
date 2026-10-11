import Foundation
#if canImport(Network)
import Network
#endif

public struct AnsightConnectivityCaptureOptions: Sendable, Codable, Equatable {
    public var minimumSampleIntervalMilliseconds: Int
    public init(minimumSampleIntervalMilliseconds: Int = 5_000) {
        self.minimumSampleIntervalMilliseconds = minimumSampleIntervalMilliseconds
    }
    public func validated() -> Self {
        .init(minimumSampleIntervalMilliseconds: min(300_000, max(1_000, minimumSampleIntervalMilliseconds)))
    }
}

/// Serializes path observations independently of the host connection. No probes or radio identifiers.
final class AnsightConnectivityMonitor: @unchecked Sendable {
    private let queue = DispatchQueue(label: "ai.ansight.connectivity")
    private let queueKey = DispatchSpecificKey<Bool>()
    private let options: AnsightConnectivityCaptureOptions
    private let emit: @Sendable (String, String, String) -> Void
    private let observationId = UUID().uuidString
    private let startedAt = ProcessInfo.processInfo.systemUptime
    private var sequence = 0
    private var running = false
    private var snapshot: [String: Any]?
    private var quality: [String: Any]?
    private var pendingQuality = false
    private var lastQualityTime = -Double.infinity
    private var timer: DispatchSourceTimer?
    #if canImport(Network)
    private var monitor: NWPathMonitor?
    #endif

    init(options: AnsightConnectivityCaptureOptions, emit: @escaping @Sendable (String, String, String) -> Void) {
        self.options = options.validated()
        self.emit = emit
        queue.setSpecific(key: queueKey, value: true)
    }

    func resume() {
        queue.async { [weak self] in self?.start() }
    }

    func suspend(reason: String) {
        if DispatchQueue.getSpecific(key: queueKey) == true { stop(reason: reason) }
        else { queue.sync { stop(reason: reason) } }
    }

    private func stop(reason: String) {
        guard running else { return }
        record(kind: "gap", state: ["availability": "unknown", "transports": [String]()], reason: reason)
        running = false
        timer?.cancel()
        timer = nil
        #if canImport(Network)
        monitor?.cancel()
        monitor = nil
        #endif
        snapshot = nil
        quality = nil
        pendingQuality = false
    }

    private func start() {
        guard !running else { return }
        running = true
        lastQualityTime = -Double.infinity
        let initial: [String: Any] = ["availability": "unknown", "transports": [String]()]
        snapshot = initial
        record(kind: "snapshot", state: initial, reason: "observation-started")
        #if canImport(Network)
        let pathMonitor = NWPathMonitor()
        monitor = pathMonitor
        pathMonitor.pathUpdateHandler = { [weak self, weak pathMonitor] path in
            guard let self, let pathMonitor, running, monitor === pathMonitor else { return }
            observe(path)
        }
        pathMonitor.start(queue: queue)
        #endif
        let source = DispatchSource.makeTimerSource(queue: queue)
        source.schedule(deadline: .now() + .milliseconds(options.minimumSampleIntervalMilliseconds),
                        repeating: .milliseconds(options.minimumSampleIntervalMilliseconds))
        source.setEventHandler { [weak self] in
            guard let self, running, pendingQuality, let snapshot else { return }
            record(kind: "quality", state: snapshot)
            pendingQuality = false
            lastQualityTime = ProcessInfo.processInfo.systemUptime
        }
        timer = source
        source.resume()
    }

    #if canImport(Network)
    private func observe(_ path: NWPath) {
        var transports: [String] = []
        if path.usesInterfaceType(.wifi) { transports.append("wifi") }
        if path.usesInterfaceType(.cellular) { transports.append("cellular") }
        if path.usesInterfaceType(.wiredEthernet) { transports.append("ethernet") }
        if path.usesInterfaceType(.other) || path.usesInterfaceType(.loopback) { transports.append("other") }
        let availability = path.status == .satisfied ? "available" : path.status == .unsatisfied ? "unavailable" : "unknown"
        let state: [String: Any] = [
            "availability": availability,
            "transports": availability == "unavailable" ? [] : transports,
            "expensive": path.isExpensive,
            "lowDataMode": path.isConstrained
        ]
        // NWPath does not establish internet validation, captive portals, VPN absence or billing status.
        quality = nil
        #if compiler(>=6.2)
        if #available(iOS 26.0, macOS 26.0, tvOS 26.0, watchOS 26.0, visionOS 26.0, *), path.status == .satisfied {
            let label: String
            switch path.linkQuality {
            case .good: label = "good"
            case .moderate: label = "moderate"
            case .minimal: label = "minimal"
            default: label = "unknown"
            }
            quality = ["source": "apple.nwpath", "measuredAtUtc": AnsightClock.isoNow(), "linkQuality": label]
        }
        #endif
        let previous = snapshot
        snapshot = state
        if previous == nil || !NSDictionary(dictionary: previous!).isEqual(to: state) {
            record(kind: previous == nil ? "snapshot" : "change", state: state, previous: previous)
            pendingQuality = false
            lastQualityTime = ProcessInfo.processInfo.systemUptime
        } else if quality != nil {
            if ProcessInfo.processInfo.systemUptime - lastQualityTime >= Double(options.minimumSampleIntervalMilliseconds) / 1_000 {
                record(kind: "quality", state: state)
                lastQualityTime = ProcessInfo.processInfo.systemUptime
                pendingQuality = false
            } else { pendingQuality = true }
        }
    }
    #endif

    private func record(kind: String, state: [String: Any], previous: [String: Any]? = nil, reason: String? = nil) {
        sequence += 1
        let id = UUID().uuidString
        var supported = ["availability", "transports", "expensive", "lowDataMode"]
        #if compiler(>=6.2)
        if #available(iOS 26.0, macOS 26.0, tvOS 26.0, watchOS 26.0, visionOS 26.0, *) { supported.append("linkQuality") }
        #endif
        var value: [String: Any] = [
            "schema": "ansight.connectivity.v1", "id": id, "observationId": observationId,
            "sequence": sequence, "capturedAtUtc": AnsightClock.isoNow(),
            "elapsedMilliseconds": max(0, (ProcessInfo.processInfo.systemUptime - startedAt) * 1_000),
            "kind": kind, "source": "apple.nwpath", "snapshot": state, "supportedMeasurements": supported
        ]
        value["previousSnapshot"] = previous
        value["quality"] = kind == "gap" ? nil : quality
        value["reason"] = reason
        guard let data = try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys]),
              let json = String(data: data, encoding: .utf8) else { return }
        emit("connectivity.\(kind)", json, id)
    }
}
