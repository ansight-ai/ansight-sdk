import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceApplicationProfile: Sendable, Codable, Equatable {
    public var appId: String?
    public var appName: String?
    public var icon: DeviceApplicationIconProfile?
    public var processId: Int?
    public var versionName: String?
    public var versionCode: String?
    public var buildNumber: String?
    public var environmentCode: Int?
    public var installSource: String?
    public var firstInstallTimeMs: Int64?
    public var lastUpdateTimeMs: Int64?
    public var debuggable: Bool?
}
