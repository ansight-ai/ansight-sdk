import Foundation

struct AnsightScreenDescriptor: Sendable, Equatable {
    let name: String
    let key: String
    let details: [String: String]
}

