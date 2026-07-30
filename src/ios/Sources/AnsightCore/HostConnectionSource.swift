import Foundation

public enum HostConnectionSource: String, Sendable, Codable, CaseIterable {
    case none
    case autoProbe
    case cachedSession
    case savedConfig
    case bundledConfig
    case payload
    case configReader
    case hostConnection
    case transport
    case telemetry
    case appState
    case sessionJpegCapture
    case touchCapture
}
