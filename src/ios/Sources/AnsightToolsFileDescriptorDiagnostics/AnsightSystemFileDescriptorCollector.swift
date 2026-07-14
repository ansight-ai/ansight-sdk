#if canImport(CAnsightFileDescriptorDiagnostics)
import CAnsightFileDescriptorDiagnostics
#endif
import Darwin
import Foundation

internal struct AnsightSystemFileDescriptorCollector: AnsightFileDescriptorCollecting {
    func snapshot(options: AnsightFileDescriptorDiagnosticsOptions) throws -> AnsightFileDescriptorSnapshot {
        let currentLimits = try limits()
        let softLimit = scanSoftLimit(currentLimits, options: options)
        let scanLimit = descriptorScanLimit(softLimit, options: options)
        var descriptors: [AnsightFileDescriptorInfo] = []

        for descriptor in 0..<scanLimit {
            if let info = try inspect(descriptor: descriptor, includeTarget: options.includeTargets) {
                descriptors.append(info)
            }
        }

        return AnsightFileDescriptorSnapshot(
            descriptors: descriptors,
            limits: currentLimits,
            scanComplete: softLimit <= UInt64(scanLimit),
            scannedDescriptorLimit: scanLimit
        )
    }

    func count(options: AnsightFileDescriptorDiagnosticsOptions) throws -> AnsightFileDescriptorCountSnapshot {
        let currentLimits = try limits()
        let softLimit = scanSoftLimit(currentLimits, options: options)
        let scanLimit = descriptorScanLimit(softLimit, options: options)
        var count = 0

        for descriptor in 0..<scanLimit {
            let openResult = ansight_fd_is_open(Int32(descriptor))
            if openResult > 0 {
                count += 1
            } else if openResult < 0 {
                throw AnsightFileDescriptorDiagnosticsError.systemCallFailed(
                    operation: "fcntl(F_GETFD)",
                    errorCode: -openResult
                )
            }
        }

        return AnsightFileDescriptorCountSnapshot(
            count: count,
            limits: currentLimits,
            scanComplete: softLimit <= UInt64(scanLimit),
            scannedDescriptorLimit: scanLimit
        )
    }

    func inspect(descriptor: Int, includeTarget: Bool) throws -> AnsightFileDescriptorInfo? {
        guard descriptor >= 0, descriptor <= Int(Int32.max) else {
            return nil
        }

        let rawDescriptor = Int32(descriptor)
        let openResult = ansight_fd_is_open(rawDescriptor)
        if openResult == 0 {
            return nil
        }
        if openResult < 0 {
            throw AnsightFileDescriptorDiagnosticsError.systemCallFailed(
                operation: "fcntl(F_GETFD)",
                errorCode: -openResult
            )
        }

        let rawDescriptorFlags = ansight_fd_descriptor_flags(rawDescriptor)
        let rawStatusFlags = ansight_fd_status_flags(rawDescriptor)
        let rawKind = ansight_fd_kind(rawDescriptor)
        let target = includeTarget ? descriptorPath(rawDescriptor) : nil

        var positionError: Int32 = 0
        let rawPosition = ansight_fd_position(rawDescriptor, &positionError)
        var inodeError: Int32 = 0
        let rawInode = ansight_fd_inode(rawDescriptor, &inodeError)

        return AnsightFileDescriptorInfo(
            descriptor: descriptor,
            kind: descriptorKind(rawKind),
            target: target,
            accessMode: accessMode(rawStatusFlags),
            closeOnExec: rawDescriptorFlags >= 0 ? (rawDescriptorFlags & FD_CLOEXEC) != 0 : nil,
            descriptorFlags: rawDescriptorFlags >= 0 ? Int(rawDescriptorFlags) : nil,
            statusFlags: rawStatusFlags >= 0 ? Int(rawStatusFlags) : nil,
            positionBytes: positionError == 0 ? rawPosition : nil,
            inode: inodeError == 0 ? rawInode : nil
        )
    }

    func limits() throws -> AnsightFileDescriptorLimits {
        var softLimit: UInt64 = 0
        var hardLimit: UInt64 = 0
        var hardLimitUnlimited: Int32 = 0
        let result = ansight_fd_limits(&softLimit, &hardLimit, &hardLimitUnlimited)
        if result < 0 {
            throw AnsightFileDescriptorDiagnosticsError.systemCallFailed(
                operation: "getrlimit(RLIMIT_NOFILE)",
                errorCode: -result
            )
        }

        return AnsightFileDescriptorLimits(
            softLimit: softLimit,
            hardLimit: hardLimitUnlimited == 0 ? hardLimit : nil,
            hardLimitUnlimited: hardLimitUnlimited != 0
        )
    }

    private func descriptorPath(_ descriptor: Int32) -> String? {
        let bufferSize = max(Int(ansight_fd_path_buffer_size()), 1)
        var buffer = [CChar](repeating: 0, count: bufferSize)
        let result = buffer.withUnsafeMutableBufferPointer { pointer in
            ansight_fd_path(descriptor, pointer.baseAddress, pointer.count)
        }
        guard result == 0 else {
            return nil
        }
        let bytes = buffer.prefix { $0 != 0 }.map { UInt8(bitPattern: $0) }
        return String(decoding: bytes, as: UTF8.self)
    }

    private func scanSoftLimit(
        _ limits: AnsightFileDescriptorLimits,
        options: AnsightFileDescriptorDiagnosticsOptions
    ) -> UInt64 {
        limits.softLimit ?? UInt64(options.maximumScannedDescriptors)
    }

    private func descriptorScanLimit(
        _ softLimit: UInt64,
        options: AnsightFileDescriptorDiagnosticsOptions
    ) -> Int {
        Int(min(softLimit, UInt64(options.maximumScannedDescriptors)))
    }

    private func descriptorKind(_ rawKind: Int32) -> AnsightFileDescriptorKind {
        switch Int(rawKind) {
        case ANSIGHT_FD_KIND_REGULAR_FILE: return .regularFile
        case ANSIGHT_FD_KIND_DIRECTORY: return .directory
        case ANSIGHT_FD_KIND_SOCKET: return .socket
        case ANSIGHT_FD_KIND_PIPE: return .pipe
        case ANSIGHT_FD_KIND_CHARACTER_DEVICE: return .characterDevice
        case ANSIGHT_FD_KIND_BLOCK_DEVICE: return .blockDevice
        case ANSIGHT_FD_KIND_SYMBOLIC_LINK: return .symbolicLink
        case ANSIGHT_FD_KIND_OTHER: return .other
        default: return .unknown
        }
    }

    private func accessMode(_ statusFlags: Int32) -> String? {
        guard statusFlags >= 0 else {
            return nil
        }
        switch statusFlags & O_ACCMODE {
        case O_RDONLY: return "read_only"
        case O_WRONLY: return "write_only"
        case O_RDWR: return "read_write"
        default: return "unknown"
        }
    }
}

internal enum AnsightFileDescriptorDiagnosticsError: LocalizedError {
    case invalidArgument(String)
    case systemCallFailed(operation: String, errorCode: Int32)

    var errorDescription: String? {
        switch self {
        case .invalidArgument(let message):
            return message
        case .systemCallFailed(let operation, let errorCode):
            return "\(operation) failed with errno \(errorCode)."
        }
    }
}
