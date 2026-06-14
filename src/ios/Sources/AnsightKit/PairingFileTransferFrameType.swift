import Foundation
import Network

public enum PairingFileTransferFrameType: UInt8, Sendable, Codable {
    case chunk = 1
    case complete = 2
    case error = 3
}
