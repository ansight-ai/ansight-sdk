import Foundation

struct CachedPairingProfileCollectionDocument: Sendable, Codable, Equatable {
    static let schemaName = "ansight.cached-pairing-profiles.v1"

    var schema: String
    var profiles: [CachedPairingProfileDocument]

    init(
        schema: String = CachedPairingProfileCollectionDocument.schemaName,
        profiles: [CachedPairingProfileDocument]
    ) {
        self.schema = schema
        self.profiles = profiles
    }
}
