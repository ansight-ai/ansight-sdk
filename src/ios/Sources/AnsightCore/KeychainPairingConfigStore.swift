import Foundation
import Security

final class KeychainPairingConfigStore: PairingConfigStore, @unchecked Sendable {
    private let service = "ai.ansight.ios.sdk"
    private let account: String

    init(account: String) {
        self.account = account
    }

    func load() -> String? {
        var query = baseQuery()
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        guard status == errSecSuccess, let data = result as? Data else {
            return nil
        }

        return String(data: data, encoding: .utf8)
    }

    func save(_ json: String) throws {
        guard let data = json.data(using: .utf8) else {
            throw RuntimeError.invalidInput("Saved pairing JSON could not be encoded as UTF-8.")
        }

        clear()
        var query = baseQuery()
        query[kSecValueData as String] = data
        #if os(iOS)
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        #endif

        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw RuntimeError.invalidInput("Failed to save host registration to Keychain: \(status).")
        }
    }

    func clear() {
        let query = baseQuery()
        SecItemDelete(query as CFDictionary)
    }

    private func baseQuery() -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
    }
}
