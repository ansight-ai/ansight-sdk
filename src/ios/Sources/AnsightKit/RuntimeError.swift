import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public enum RuntimeError: LocalizedError {
    case notInitialized(String)
    case invalidInput(String)

    public var errorDescription: String? {
        switch self {
        case .notInitialized(let message), .invalidInput(let message):
            return message
        }
    }
}
