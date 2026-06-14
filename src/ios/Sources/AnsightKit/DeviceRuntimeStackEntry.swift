import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceRuntimeStackEntry: Sendable, Codable, Equatable {
    public var runtimeCode: Int?
    public var name: String?
    public var version: String?
    public var layer: String?
}
