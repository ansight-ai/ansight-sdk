import Foundation
import Security

protocol PairingConfigStore: Sendable {
    func load() -> String?
    func save(_ json: String) throws
    func clear()
}
