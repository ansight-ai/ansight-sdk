import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceApplicationIconProfile: Sendable, Codable, Equatable {
    public var format: String?
    public var mimeType: String?
    public var width: Int?
    public var height: Int?
    public var byteCount: Int?
    public var dataBase64: String?

    public init(
        format: String? = nil,
        mimeType: String? = nil,
        width: Int? = nil,
        height: Int? = nil,
        byteCount: Int? = nil,
        dataBase64: String? = nil
    ) {
        self.format = format
        self.mimeType = mimeType
        self.width = width
        self.height = height
        self.byteCount = byteCount
        self.dataBase64 = dataBase64
    }
}
