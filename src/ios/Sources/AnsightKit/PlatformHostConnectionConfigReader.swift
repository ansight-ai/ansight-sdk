import Foundation

public final class PlatformHostConnectionConfigReader: HostConnectionConfigReading, @unchecked Sendable {
    public init() {}

    public func canRead(_ kind: HostConnectionRequestKind) -> Bool {
        switch kind {
        case .file, .qrCode:
            return Self.platformPairingUiAvailable
        default:
            return false
        }
    }

    public func readConfigPayload(for request: HostConnectionRequest) async throws -> String? {
        guard canRead(request.kind) else {
            throw RuntimeError.invalidInput("No platform host config reader is available for \(request.kind.rawValue).")
        }

        switch request.kind {
        case .file:
            #if canImport(UIKit) && canImport(UniformTypeIdentifiers)
            return try await PlatformPairingFilePicker.read(request: request)
            #else
            throw RuntimeError.invalidInput("File pairing import is only available on UIKit platforms.")
            #endif
        case .qrCode:
            #if canImport(UIKit) && canImport(AVFoundation)
            return try await PlatformPairingQrScannerViewController.scan(request: request)
            #else
            throw RuntimeError.invalidInput("QR pairing scan is only available on UIKit platforms.")
            #endif
        default:
            return nil
        }
    }

    private static var platformPairingUiAvailable: Bool {
        #if canImport(UIKit)
        return true
        #else
        return false
        #endif
    }
}
