import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceThermalProfile: Sendable, Codable, Equatable {
    public var statusCode: Int?
}
