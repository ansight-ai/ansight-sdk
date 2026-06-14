import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceGpuProfile: Sendable, Codable, Equatable {
    public var vendor: String?
    public var model: String?
    public var driver: String?
    public var renderer: String?
    public var apiCode: Int?
    public var driverVersion: String?
    public var vramMb: Int64?
    public var featureLevel: String?
}
