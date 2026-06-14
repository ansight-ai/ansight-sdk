import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceSdkProfile: Sendable, Codable, Equatable {
    public var name: String?
    public var packageId: String?
    public var version: String?
    public var language: String?
}
