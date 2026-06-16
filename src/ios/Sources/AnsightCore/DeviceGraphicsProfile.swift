import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceGraphicsProfile: Sendable, Codable, Equatable {
    public var renderBackendCode: Int?
    public var fpsTarget: Int?
    public var vsyncEnabled: Bool?
}
