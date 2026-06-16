import Foundation

public enum HostConnectionSummaryKind: String, Sendable, Codable, CaseIterable {
    case runtimeUnavailable
    case runtimeInactive
    case disconnectedNoConfigs
    case disconnectedCachedSessionAvailable
    case disconnectedSavedConfigAvailable
    case disconnectedBundledConfigAvailable
    case disconnectedMultipleConfigsAvailable
    case connecting
    case connected
}
