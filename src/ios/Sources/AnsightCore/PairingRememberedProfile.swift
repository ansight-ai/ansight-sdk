import Foundation

enum PairingRememberedProfile {
    static func replacingEnrollment(
        in document: ParsedPairingDocument,
        with grant: PairingGrantV2?
    ) -> ParsedPairingDocument {
        guard document.config.isSecureV2,
              let grant,
              grant.hostId == document.config.host.hostId,
              grant.configId == document.config.configId,
              grant.appId == document.config.appId,
              SecurePairingProtocol.verifyGrant(grant, hostPublicKey: document.config.host.hostPubKey)
        else {
            return document
        }

        var config = document.config
        config.schema = PairingConfig.secureRememberedProfileSchemaName
        config.issuedAt = grant.issuedAt
        config.expiresAt = grant.expiresAt
        config.enrollment = nil
        config.signature = ""
        return ParsedPairingDocument(config: config, discoveryHint: document.discoveryHint)
    }
}
