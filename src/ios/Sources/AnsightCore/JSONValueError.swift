import Foundation

public enum JSONValueError: LocalizedError {
    case invalidUTF8
    case unsupportedValue

    public var errorDescription: String? {
        switch self {
        case .invalidUTF8:
            return "JSON data could not be encoded as UTF-8."
        case .unsupportedValue:
            return "JSON value could not be represented."
        }
    }
}
