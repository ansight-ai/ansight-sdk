import Foundation

public enum JSONValue: Sendable, Codable, Equatable {
    case object([String: JSONValue])
    case array([JSONValue])
    case string(String)
    case integer(Int64)
    case number(Double)
    case bool(Bool)
    case null

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()

        if container.decodeNil() {
            self = .null
        } else if let object = try? container.decode([String: JSONValue].self) {
            self = .object(object)
        } else if let array = try? container.decode([JSONValue].self) {
            self = .array(array)
        } else if let value = try? container.decode(Bool.self) {
            self = .bool(value)
        } else if let value = try? container.decode(Int64.self) {
            self = .integer(value)
        } else if let value = try? container.decode(Double.self) {
            self = .number(value)
        } else if let value = try? container.decode(String.self) {
            self = .string(value)
        } else {
            throw DecodingError.dataCorruptedError(
                in: container,
                debugDescription: "Unsupported JSON value."
            )
        }
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()

        switch self {
        case .object(let value):
            try container.encode(value)
        case .array(let value):
            try container.encode(value)
        case .string(let value):
            try container.encode(value)
        case .integer(let value):
            try container.encode(value)
        case .number(let value):
            try container.encode(value)
        case .bool(let value):
            try container.encode(value)
        case .null:
            try container.encodeNil()
        }
    }

    public var stringValue: String? {
        switch self {
        case .string(let value):
            return value
        case .integer(let value):
            return String(value)
        case .number(let value):
            return String(value)
        case .bool(let value):
            return String(value)
        case .null:
            return nil
        case .object, .array:
            return try? jsonString()
        }
    }

    public func jsonString(prettyPrinted: Bool = false) throws -> String {
        let data = try jsonData(prettyPrinted: prettyPrinted)
        guard let string = String(data: data, encoding: .utf8) else {
            throw JSONValueError.invalidUTF8
        }

        return string
    }

    public func jsonData(prettyPrinted: Bool = false) throws -> Data {
        let options: JSONSerialization.WritingOptions = prettyPrinted ? [.prettyPrinted, .sortedKeys] : [.sortedKeys]
        return try JSONSerialization.data(withJSONObject: anyValue, options: options)
    }

    internal var anyValue: Any {
        switch self {
        case .object(let value):
            return value.mapValues(\.anyValue)
        case .array(let value):
            return value.map(\.anyValue)
        case .string(let value):
            return value
        case .integer(let value):
            return NSNumber(value: value)
        case .number(let value):
            return NSNumber(value: value)
        case .bool(let value):
            return NSNumber(value: value)
        case .null:
            return NSNull()
        }
    }

    public static func object(from dictionary: [String: String]) -> JSONValue {
        .object(dictionary.mapValues { .string($0) })
    }

    public static func object(fromGrouped dictionary: [String: [String: String]]) -> JSONValue {
        .object(dictionary.mapValues { .object(from: $0) })
    }

    public static func fromEncodable<T: Encodable>(_ value: T) throws -> JSONValue {
        let data = try JSONEncoder.ansightEncoder.encode(value)
        let object = try JSONSerialization.jsonObject(with: data, options: [])
        return try fromAny(object)
    }

    private static func fromAny(_ value: Any) throws -> JSONValue {
        switch value {
        case let object as [String: Any]:
            return .object(try object.mapValues { try fromAny($0) })
        case let array as [Any]:
            return .array(try array.map { try fromAny($0) })
        case let value as String:
            return .string(value)
        case let value as NSNumber:
            if CFGetTypeID(value) == CFBooleanGetTypeID() {
                return .bool(value.boolValue)
            }
            let doubleValue = value.doubleValue
            let int64Value = value.int64Value
            if doubleValue.rounded() == doubleValue {
                return .integer(int64Value)
            }
            return .number(doubleValue)
        case _ as NSNull:
            return .null
        default:
            throw JSONValueError.unsupportedValue
        }
    }
}
