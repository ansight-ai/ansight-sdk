import CryptoKit
import Foundation
import Security

struct PairingClientIdentity: @unchecked Sendable {
    let signingKey: PairingClientSigningKey
    var grant: PairingGrantV2?

    var publicKeyBase64: String {
        signingKey.publicKeyDER.base64EncodedString()
    }

    var keyId: String {
        SecurePairingProtocol.base64URL(Data(SHA256.hash(data: signingKey.publicKeyDER)))
    }

    func signatureBase64(for input: String) throws -> String {
        try signingKey.signature(for: Data(input.utf8)).base64EncodedString()
    }
}

enum PairingClientSigningKey: @unchecked Sendable {
    case secureEnclave(SecureEnclave.P256.Signing.PrivateKey)
    case software(P256.Signing.PrivateKey)

    static func create() throws -> PairingClientSigningKey {
        if SecureEnclave.isAvailable,
           let secureKey = try? SecureEnclave.P256.Signing.PrivateKey() {
            return .secureEnclave(secureKey)
        }
        return .software(P256.Signing.PrivateKey())
    }

    static func restore(kind: String, data: Data) throws -> PairingClientSigningKey {
        if kind == "secureEnclave" {
            return .secureEnclave(try SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: data))
        }
        guard kind == "software" else {
            throw PairingDocumentError.invalidDocument("Stored client-key type is unsupported.")
        }
        return .software(try P256.Signing.PrivateKey(rawRepresentation: data))
    }

    var kind: String {
        switch self {
        case .secureEnclave: "secureEnclave"
        case .software: "software"
        }
    }

    var persistentRepresentation: Data {
        switch self {
        case .secureEnclave(let key): key.dataRepresentation
        case .software(let key): key.rawRepresentation
        }
    }

    var publicKeyDER: Data {
        switch self {
        case .secureEnclave(let key): key.publicKey.derRepresentation
        case .software(let key): key.publicKey.derRepresentation
        }
    }

    func signature(for data: Data) throws -> Data {
        switch self {
        case .secureEnclave(let key): try key.signature(for: data).rawRepresentation
        case .software(let key): try key.signature(for: data).rawRepresentation
        }
    }
}

protocol PairingClientIdentityStoring: Sendable {
    func loadOrCreate(hostId: String, appId: String) throws -> PairingClientIdentity
    func save(_ identity: PairingClientIdentity, hostId: String, appId: String) throws
    func clear(hostId: String, appId: String)
}

final class KeychainPairingClientIdentityStore: PairingClientIdentityStoring, @unchecked Sendable {
    private static let service = "ai.ansight.ios.sdk.secure-pairing"
    private let lock = NSLock()

    func loadOrCreate(hostId: String, appId: String) throws -> PairingClientIdentity {
        try lock.withLock {
            if let stored = load(hostId: hostId, appId: appId),
               let keyData = Data(base64Encoded: stored.keyData),
               let key = try? PairingClientSigningKey.restore(kind: stored.keyKind, data: keyData) {
                return PairingClientIdentity(signingKey: key, grant: stored.grant)
            }

            let identity = PairingClientIdentity(signingKey: try .create(), grant: nil)
            try saveLocked(identity, hostId: hostId, appId: appId)
            return identity
        }
    }

    func save(_ identity: PairingClientIdentity, hostId: String, appId: String) throws {
        try lock.withLock {
            try saveLocked(identity, hostId: hostId, appId: appId)
        }
    }

    func clear(hostId: String, appId: String) {
        lock.withLock {
            SecItemDelete(baseQuery(hostId: hostId, appId: appId) as CFDictionary)
        }
    }

    private func load(hostId: String, appId: String) -> StoredPairingClientIdentity? {
        var query = baseQuery(hostId: hostId, appId: appId)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data
        else {
            return nil
        }
        return try? JSONDecoder.ansightDecoder.decode(StoredPairingClientIdentity.self, from: data)
    }

    private func saveLocked(_ identity: PairingClientIdentity, hostId: String, appId: String) throws {
        let stored = StoredPairingClientIdentity(
            keyKind: identity.signingKey.kind,
            keyData: identity.signingKey.persistentRepresentation.base64EncodedString(),
            grant: identity.grant
        )
        let data = try JSONEncoder.ansightEncoder.encode(stored)
        let base = baseQuery(hostId: hostId, appId: appId)
        SecItemDelete(base as CFDictionary)
        var query = base
        query[kSecValueData as String] = data
        #if os(iOS)
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        #endif
        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw PairingDocumentError.invalidDocument("Failed to persist secure pairing identity: \(status).")
        }
    }

    private func baseQuery(hostId: String, appId: String) -> [String: Any] {
        let accountData = Data("\(hostId)|\(appId)".utf8)
        let account = SecurePairingProtocol.base64URL(Data(SHA256.hash(data: accountData)))
        return [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: Self.service,
            kSecAttrAccount as String: account,
        ]
    }
}

private struct StoredPairingClientIdentity: Codable {
    let keyKind: String
    let keyData: String
    let grant: PairingGrantV2?
}
