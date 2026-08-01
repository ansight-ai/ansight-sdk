actor AnsightLatestValueBuffer<Element: Sendable> {
    private var pendingValue: Element?
    private var waitingConsumer: CheckedContinuation<Element?, Never>?
    private var isFinished = false

    @discardableResult
    func submit(_ value: Element) -> Bool {
        guard !isFinished else {
            return false
        }

        if let waitingConsumer {
            self.waitingConsumer = nil
            waitingConsumer.resume(returning: value)
            return false
        }

        let replacedPendingValue = pendingValue != nil
        pendingValue = value
        return replacedPendingValue
    }

    func next() async -> Element? {
        if let pendingValue {
            self.pendingValue = nil
            return pendingValue
        }

        guard !isFinished else {
            return nil
        }

        return await withCheckedContinuation { continuation in
            waitingConsumer = continuation
        }
    }

    func finish() {
        guard !isFinished else {
            return
        }

        isFinished = true
        pendingValue = nil
        let waitingConsumer = waitingConsumer
        self.waitingConsumer = nil
        waitingConsumer?.resume(returning: nil)
    }
}
