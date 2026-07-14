import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceProfile: Sendable, Codable, Equatable {
    public var nativeDeviceId: String? = nil
    public var manufacturer: String?
    public var brand: String?
    public var model: String?
    public var product: String?
    public var formFactor: String?
    public var deviceClassCode: Int?
    public var isVirtual: Bool?
    public var isEmulator: Bool?
    public var locale: String?
    public var timeZone: String?
    public var osName: String?
    public var osVersion: String?
    public var osBuild: String?
    public var apiLevel: Int?
    public var cpuArch: String?
    public var cpuCoreCount: Int?
    public var abiList: [String]?
    public var chipModel: String?
    public var memoryTotalMb: Int64?
    public var memoryFreeMb: Int64?
    public var storageTotalMb: Int64?
    public var storageFreeMb: Int64?
    public var battery: DeviceBatteryProfile?
    public var display: DeviceDisplayProfile?
    public var gpu: DeviceGpuProfile?
    public var network: DeviceNetworkProfile?
    public var thermal: DeviceThermalProfile?
}
