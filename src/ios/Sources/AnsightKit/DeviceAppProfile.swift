import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceAppProfile: Sendable, Codable, Equatable {
    public var type: String
    public var schema: String
    public var sentAt: Int64
    public var reasonCode: Int
    public var profileSeq: Int
    public var sdk: DeviceSdkProfile?
    public var device: DeviceProfile?
    public var app: DeviceApplicationProfile?
    public var runtime: DeviceRuntimeProfile?
    public var graphics: DeviceGraphicsProfile?
    public var permissions: [String: String]?
    public var tags: [String]?

    public init(
        type: String = "DeviceAppProfile",
        schema: String = "ansight.device-app-profile.v1",
        sentAt: Int64 = Int64(Date().timeIntervalSince1970 * 1_000),
        reasonCode: Int = 1,
        profileSeq: Int = 1,
        sdk: DeviceSdkProfile? = nil,
        device: DeviceProfile? = nil,
        app: DeviceApplicationProfile? = nil,
        runtime: DeviceRuntimeProfile? = nil,
        graphics: DeviceGraphicsProfile? = nil,
        permissions: [String: String]? = nil,
        tags: [String]? = nil
    ) {
        self.type = type
        self.schema = schema
        self.sentAt = sentAt
        self.reasonCode = reasonCode
        self.profileSeq = profileSeq
        self.sdk = sdk
        self.device = device
        self.app = app
        self.runtime = runtime
        self.graphics = graphics
        self.permissions = permissions
        self.tags = tags
    }
}
