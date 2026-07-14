import AnsightCore
import Foundation

internal enum AnsightFileDescriptorKind: String, Sendable, CaseIterable {
    case regularFile = "regular_file"
    case directory
    case socket
    case pipe
    case characterDevice = "character_device"
    case blockDevice = "block_device"
    case symbolicLink = "symbolic_link"
    case anonymousInode = "anonymous_inode"
    case memoryFile = "memory_file"
    case other
    case unknown
}

internal struct AnsightFileDescriptorInfo: Sendable, Equatable {
    let descriptor: Int
    let kind: AnsightFileDescriptorKind
    let target: String?
    let accessMode: String?
    let closeOnExec: Bool?
    let descriptorFlags: Int?
    let statusFlags: Int?
    let positionBytes: Int64?
    let inode: UInt64?

    var jsonValue: JSONValue {
        let inodeValue: JSONValue
        if let inode, inode <= UInt64(Int64.max) {
            inodeValue = .integer(Int64(inode))
        } else {
            inodeValue = .null
        }
        let object: [String: JSONValue] = [
            "descriptor": .integer(Int64(descriptor)),
            "kind": .string(kind.rawValue),
            "target": target.map(JSONValue.string) ?? .null,
            "accessMode": accessMode.map(JSONValue.string) ?? .null,
            "closeOnExec": closeOnExec.map(JSONValue.bool) ?? .null,
            "descriptorFlags": descriptorFlags.map { .integer(Int64($0)) } ?? .null,
            "statusFlags": statusFlags.map { .integer(Int64($0)) } ?? .null,
            "positionBytes": positionBytes.map(JSONValue.integer) ?? .null,
            "inode": inodeValue,
        ]
        return .object(object)
    }
}

internal struct AnsightFileDescriptorLimits: Sendable, Equatable {
    let softLimit: UInt64?
    let hardLimit: UInt64?
    let hardLimitUnlimited: Bool
}

internal struct AnsightFileDescriptorSnapshot: Sendable, Equatable {
    let descriptors: [AnsightFileDescriptorInfo]
    let limits: AnsightFileDescriptorLimits
    let scanComplete: Bool
    let scannedDescriptorLimit: Int
}

internal struct AnsightFileDescriptorCountSnapshot: Sendable, Equatable {
    let count: Int
    let limits: AnsightFileDescriptorLimits
    let scanComplete: Bool
    let scannedDescriptorLimit: Int
}

internal protocol AnsightFileDescriptorCollecting: Sendable {
    func snapshot(options: AnsightFileDescriptorDiagnosticsOptions) throws -> AnsightFileDescriptorSnapshot
    func count(options: AnsightFileDescriptorDiagnosticsOptions) throws -> AnsightFileDescriptorCountSnapshot
    func inspect(descriptor: Int, includeTarget: Bool) throws -> AnsightFileDescriptorInfo?
    func limits() throws -> AnsightFileDescriptorLimits
}
