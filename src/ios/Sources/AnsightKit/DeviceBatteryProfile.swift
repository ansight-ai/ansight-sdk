import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceBatteryProfile: Sendable, Codable, Equatable {
    public var levelPct: Int?
    public var stateCode: Int?
    public var healthCode: Int?
    public var temperatureC: Double?
}
