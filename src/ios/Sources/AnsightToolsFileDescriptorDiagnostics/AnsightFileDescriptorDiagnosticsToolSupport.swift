import AnsightCore
import Foundation

internal enum AnsightFileDescriptorDiagnosticsToolSupport {
    static func snapshotMetadata(_ snapshot: AnsightFileDescriptorSnapshot) -> [String: JSONValue] {
        snapshotMetadata(
            scanComplete: snapshot.scanComplete,
            scannedDescriptorLimit: snapshot.scannedDescriptorLimit
        )
    }

    static func snapshotMetadata(_ snapshot: AnsightFileDescriptorCountSnapshot) -> [String: JSONValue] {
        snapshotMetadata(
            scanComplete: snapshot.scanComplete,
            scannedDescriptorLimit: snapshot.scannedDescriptorLimit
        )
    }

    private static func snapshotMetadata(
        scanComplete: Bool,
        scannedDescriptorLimit: Int
    ) -> [String: JSONValue] {
        [
            "scanComplete": .bool(scanComplete),
            "scannedDescriptorLimit": .integer(Int64(scannedDescriptorLimit)),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ]
    }

    static func integer(
        _ arguments: [String: String],
        key: String,
        defaultValue: Int? = nil,
        minimum: Int,
        maximum: Int
    ) throws -> Int {
        guard let rawValue = arguments[key]?.trimmingCharacters(in: .whitespacesAndNewlines), !rawValue.isEmpty else {
            if let defaultValue {
                return min(max(defaultValue, minimum), maximum)
            }
            throw AnsightFileDescriptorDiagnosticsError.invalidArgument("Argument '\(key)' is required.")
        }
        guard let value = Int(rawValue), value >= minimum, value <= maximum else {
            throw AnsightFileDescriptorDiagnosticsError.invalidArgument(
                "Argument '\(key)' must be an integer between \(minimum) and \(maximum)."
            )
        }
        return value
    }

    static func optionalInt64(_ value: UInt64?) -> JSONValue {
        guard let value, value <= UInt64(Int64.max) else {
            return .null
        }
        return .integer(Int64(value))
    }
}
