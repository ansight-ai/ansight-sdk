import Foundation
import Security

extension HarnessViewModel {
    func prepareHarnessSecureStorageSample() throws {
        let data = Data("native-harness-secret-\(seededAtUtc)".utf8)
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: HarnessConstants.secureStorageService,
            kSecAttrAccount as String: HarnessConstants.secureStorageKey,
        ]

        SecItemDelete(query as CFDictionary)

        var item = query
        item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        let status = SecItemAdd(item as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw harnessError("Unable to seed harness secure storage: OSStatus \(status).")
        }
    }
}
