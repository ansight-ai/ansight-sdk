import Foundation

struct AnsightTouchBatchKey: Sendable, Hashable {
    let space: String
    let unit: String
    let surfaceWidth: Double?
    let surfaceHeight: Double?
    let surfaceScale: Double?
}
