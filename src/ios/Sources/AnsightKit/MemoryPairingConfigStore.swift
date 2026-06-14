import Foundation
import Security

final class MemoryPairingConfigStore: PairingConfigStore, @unchecked Sendable {
    private let lock = NSLock()
    private var value: String?

    func load() -> String? {
        lock.withLock { value }
    }

    func save(_ json: String) {
        lock.withLock {
            value = json
        }
    }

    func clear() {
        lock.withLock {
            value = nil
        }
    }
}
