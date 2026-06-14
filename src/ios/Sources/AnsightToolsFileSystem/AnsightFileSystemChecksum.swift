import Foundation

internal struct AnsightFileSystemChecksum: Sendable, Equatable {
    let algorithm: String
    let checksum: String
    let encoding: String
}
