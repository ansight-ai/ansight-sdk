import Ansight
import Foundation

@objc(ANSAnsight)
public final class ANSAnsight: NSObject {
    @objc public static var isInitialized: Bool {
        AnsightRuntime.shared.snapshot().initialized
    }

    @objc public static var isActive: Bool {
        AnsightRuntime.shared.snapshot().active
    }

    @objc public static var isSessionOpen: Bool {
        AnsightRuntime.shared.snapshot().sessionOpen
    }

    @objc(initializeAndActivateWithDefaultOptionsAndReturnError:)
    public static func initializeAndActivateWithDefaultOptions() throws {
        try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
    }

    @objc(initializeAndActivateWithPairingConfigJson:clientName:error:)
    public static func initializeAndActivate(pairingConfigJson: String?, clientName: String?) throws {
        var options = AnsightOptions.ansightDeveloperDefaults
        if let pairingConfigJson = normalized(pairingConfigJson) {
            options.hostConnection.bundledDeveloperConfigJson = pairingConfigJson
        }
        options.hostAutoProbe.clientName = normalized(clientName)

        try AnsightRuntime.shared.initializeAndActivateAnsightSdk(options: options)
    }

    @objc public static func activate() throws {
        try AnsightRuntime.shared.activate()
    }

    @objc public static func deactivate() {
        AnsightRuntime.shared.deactivate()
    }

    @objc public static func clear() {
        AnsightRuntime.shared.clear()
    }

    @objc(registerMetricChannelWithId:name:unit:type:colorHex:error:)
    public static func registerMetricChannel(
        id: Int,
        name: String,
        unit: String?,
        type: String?,
        colorHex: String?
    ) throws {
        try AnsightRuntime.shared.registerMetricChannel(
            AnsightChannel(
                id: id,
                name: name,
                colorHex: normalized(colorHex),
                unit: normalized(unit),
                type: normalized(type) ?? "custom"
            )
        )
    }

    @objc(registerMetricStreamWithId:name:unit:type:colorHex:sampler:error:)
    public static func registerMetricStream(
        id: Int,
        name: String,
        unit: String?,
        type: String?,
        colorHex: String?,
        sampler: @escaping @Sendable () -> NSNumber?
    ) throws {
        let channel = AnsightChannel(
            id: id,
            name: name,
            colorHex: normalized(colorHex),
            unit: normalized(unit),
            type: normalized(type) ?? "custom"
        )
        try AnsightRuntime.shared.registerMetricStream(
            AnsightMetricStream(channel: channel) {
                sampler()?.int64Value
            }
        )
    }

    @objc(recordMetric:channel:error:)
    public static func recordMetric(_ value: Int64, channel: Int) throws {
        try AnsightRuntime.shared.metric(value, channel: channel)
    }

    @objc(recordEventWithLabel:type:details:channel:error:)
    public static func recordEvent(label: String, type: String?, details: String?, channel: Int) throws {
        try AnsightRuntime.shared.event(
            label,
            type: eventType(from: type),
            details: normalized(details),
            channel: channel
        )
    }

    @objc(screenViewedWithName:details:error:)
    public static func screenViewed(name: String, details: NSDictionary?) throws {
        try AnsightRuntime.shared.screenViewed(name, details: ANSJSONBridge.stringDictionary(from: details))
    }

    @objc(setAppLifecycleState:)
    public static func setAppLifecycleState(_ state: String) {
        AnsightRuntime.shared.setAppLifecycleState(lifecycleState(from: state))
    }

    @objc public static func setForeground() {
        AnsightRuntime.shared.setAppLifecycleState(.foreground)
    }

    @objc public static func setBackground() {
        AnsightRuntime.shared.setAppLifecycleState(.background)
    }

    @objc(connectWithPairingJson:clientName:completion:)
    public static func connect(
        pairingJson: String,
        clientName: String?,
        completion: @escaping @Sendable (ANSHostConnectionResult) -> Void
    ) {
        Task {
            let result = await AnsightRuntime.shared.connect(.payloadText(pairingJson, clientName: normalized(clientName)))
            completion(ANSHostConnectionResult(result))
        }
    }

    @objc(sendClientLog:completion:)
    public static func sendClientLog(
        _ logLine: String,
        completion: @escaping @Sendable (ANSOperationResult) -> Void
    ) {
        Task {
            let result = await AnsightRuntime.shared.sendClientLog(logLine)
            completion(ANSOperationResult(result))
        }
    }

    @objc(updateSessionProperties:completion:)
    public static func updateSessionProperties(
        _ customProperties: NSDictionary?,
        completion: @escaping @Sendable (ANSOperationResult) -> Void
    ) {
        let properties = ANSJSONBridge.groupedStringDictionary(from: customProperties)
        Task {
            let result = await AnsightRuntime.shared.updateSessionProperties(properties)
            completion(ANSOperationResult(result))
        }
    }

    @objc(clearSessionPropertiesWithCompletion:)
    public static func clearSessionProperties(completion: @escaping @Sendable (ANSOperationResult) -> Void) {
        Task {
            let result = await AnsightRuntime.shared.clearSessionProperties()
            completion(ANSOperationResult(result))
        }
    }

    @objc(registerVisualTreeProvider:error:)
    public static func registerVisualTreeProvider(_ provider: ANSVisualTreeProvider) throws {
        try AnsightVisualTreeProviderRegistry.register(provider)
    }

    @objc public static func registeredVisualTreeSources() -> [String] {
        AnsightVisualTreeProviderRegistry.registeredSources()
    }

    @objc public static func snapshot() -> NSDictionary {
        ANSJSONBridge.dictionary(from: AnsightRuntime.shared.snapshot())
    }

    private static func normalized(_ value: String?) -> String? {
        let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed?.isEmpty == false ? trimmed : nil
    }

    private static func eventType(from rawValue: String?) -> AnsightEventType {
        guard let normalized = normalized(rawValue)?.lowercased() else {
            return .info
        }

        return AnsightEventType(rawValue: normalized) ?? .info
    }

    private static func lifecycleState(from rawValue: String) -> AppLifecycleState {
        switch rawValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case AppLifecycleState.foreground.rawValue:
            return .foreground
        case AppLifecycleState.background.rawValue:
            return .background
        default:
            return .unknown
        }
    }
}
