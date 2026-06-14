import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceDisplayProfile: Sendable, Codable, Equatable {
    public var widthPx: Int?
    public var heightPx: Int?
    public var densityDpi: Int?
    public var refreshRateHz: Double?
    public var hdrSupported: Bool?
}
