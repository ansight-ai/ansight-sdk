import CryptoKit
import Foundation

extension Array where Element == String {
    func joined(prefix: String, suffix: String) -> String {
        prefix + joined(separator: ",") + suffix
    }
}
