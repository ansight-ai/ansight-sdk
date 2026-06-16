import Foundation

struct RuntimeConnectionTask: Sendable {
    let id: UUID
    let task: Task<HostConnectionResult, Never>
    let created: Bool
}
