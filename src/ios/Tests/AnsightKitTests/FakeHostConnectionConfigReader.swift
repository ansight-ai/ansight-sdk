import Foundation
@testable import AnsightKit

final class FakeHostConnectionConfigReader: HostConnectionConfigReading, @unchecked Sendable {
    private let lock = NSLock()
    private let supportedKinds: Set<HostConnectionRequestKind>
    private let payload: String?
    private var requests: [HostConnectionRequest] = []

    init(supportedKinds: Set<HostConnectionRequestKind>, payload: String?) {
        self.supportedKinds = supportedKinds
        self.payload = payload
    }

    var readRequestKinds: [HostConnectionRequestKind] {
        lock.withLock { requests.map(\.kind) }
    }

    func canRead(_ kind: HostConnectionRequestKind) -> Bool {
        supportedKinds.contains(kind)
    }

    func readConfigPayload(for request: HostConnectionRequest) async throws -> String? {
        lock.withLock {
            requests.append(request)
        }
        return payload
    }
}
