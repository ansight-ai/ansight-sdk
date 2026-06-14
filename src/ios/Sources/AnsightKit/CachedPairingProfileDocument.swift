import Foundation

struct CachedPairingProfileDocument: Sendable, Codable, Equatable {
    static let schemaName = "ansight.cached-pairing-profile.v1"

    var schema: String
    var cachedAtUtc: String
    var expiresAtUtc: String
    var document: PairingConfigDocument

    init(
        schema: String = CachedPairingProfileDocument.schemaName,
        cachedAtUtc: String,
        expiresAtUtc: String,
        document: PairingConfigDocument
    ) {
        self.schema = schema
        self.cachedAtUtc = cachedAtUtc
        self.expiresAtUtc = expiresAtUtc
        self.document = document
    }
}
