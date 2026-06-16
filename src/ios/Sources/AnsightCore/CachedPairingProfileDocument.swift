import Foundation

struct CachedPairingProfileDocument: Sendable, Codable, Equatable {
    static let schemaName = "ansight.cached-pairing-profile.v1"

    var schema: String
    var networkKey: String?
    var wifiName: String?
    var hostName: String?
    var cachedAtUtc: String
    var expiresAtUtc: String
    var document: PairingConfigDocument

    init(
        schema: String = CachedPairingProfileDocument.schemaName,
        networkKey: String? = nil,
        wifiName: String? = nil,
        hostName: String? = nil,
        cachedAtUtc: String,
        expiresAtUtc: String,
        document: PairingConfigDocument
    ) {
        self.schema = schema
        self.networkKey = networkKey
        self.wifiName = wifiName
        self.hostName = hostName
        self.cachedAtUtc = cachedAtUtc
        self.expiresAtUtc = expiresAtUtc
        self.document = document
    }
}
