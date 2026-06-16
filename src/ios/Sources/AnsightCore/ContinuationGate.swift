import Foundation
import Network

final class ContinuationGate<Value: Sendable>: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<Value, Error>?

    init(continuation: CheckedContinuation<Value, Error>) {
        self.continuation = continuation
    }

    func resume(_ result: Result<Value, Error>) {
        let pending = lock.withLock { () -> CheckedContinuation<Value, Error>? in
            let pending = continuation
            continuation = nil
            return pending
        }

        guard let pending else {
            return
        }

        switch result {
        case .success(let value):
            pending.resume(returning: value)
        case .failure(let error):
            pending.resume(throwing: error)
        }
    }
}
