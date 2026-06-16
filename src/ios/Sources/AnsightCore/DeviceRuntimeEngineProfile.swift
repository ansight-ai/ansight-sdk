import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceRuntimeEngineProfile: Sendable, Codable, Equatable {
    public var name: String?
    public var version: String?
    public var metadata: [String: String]?
}
