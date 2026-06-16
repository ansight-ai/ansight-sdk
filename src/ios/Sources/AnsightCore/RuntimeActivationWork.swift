import Foundation

struct RuntimeActivationWork: Sendable {
    let shouldStartAutoProbe: Bool
    let shouldStartDeveloperConnect: Bool
    let clientName: String?
}
