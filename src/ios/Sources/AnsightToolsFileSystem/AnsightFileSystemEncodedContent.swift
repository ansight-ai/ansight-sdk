import Foundation

internal struct AnsightFileSystemEncodedContent: Sendable, Equatable {
    let requestedEncoding: String
    let contentType: String
    let encoding: String
    let text: String?
    let base64: String?
}
