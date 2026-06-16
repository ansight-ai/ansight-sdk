import CryptoKit
import Foundation

public enum PairingDocumentError: LocalizedError {
    case invalidDocument(String)

    public var errorDescription: String? {
        switch self {
        case .invalidDocument(let message):
            return message
        }
    }
}
