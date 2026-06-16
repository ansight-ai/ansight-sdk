import Ansight
import Foundation

enum ANSJSONBridge {
    static func jsonValue(from dictionary: NSDictionary) throws -> JSONValue {
        guard JSONSerialization.isValidJSONObject(dictionary) else {
            throw NSError(
                domain: "ai.ansight.objc",
                code: 1,
                userInfo: [NSLocalizedDescriptionKey: "Dictionary contains values that cannot be encoded as JSON."]
            )
        }

        let data = try JSONSerialization.data(withJSONObject: dictionary, options: [])
        return try JSONDecoder().decode(JSONValue.self, from: data)
    }

    static func dictionary<T: Encodable>(from value: T) -> NSDictionary {
        guard let data = try? JSONEncoder().encode(value),
              let object = try? JSONSerialization.jsonObject(with: data, options: []),
              let dictionary = object as? NSDictionary
        else {
            return [:]
        }

        return dictionary
    }

    static func stringDictionary(from dictionary: NSDictionary?) -> [String: String] {
        guard let dictionary else {
            return [:]
        }

        var result: [String: String] = [:]
        for (key, value) in dictionary {
            guard let key = key as? String else {
                continue
            }

            if let value = value as? String {
                result[key] = value
            } else if let value = value as? NSNumber {
                result[key] = value.stringValue
            }
        }

        return result
    }

    static func groupedStringDictionary(from dictionary: NSDictionary?) -> [String: [String: String]] {
        guard let dictionary else {
            return [:]
        }

        var result: [String: [String: String]] = [:]
        for (key, value) in dictionary {
            guard let key = key as? String,
                  let nested = value as? NSDictionary
            else {
                continue
            }

            let values = stringDictionary(from: nested)
            if !values.isEmpty {
                result[key] = values
            }
        }

        return result
    }
}
