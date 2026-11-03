import Ansight
import Foundation

extension HarnessViewModel {
    var offlineUploadEnabled: Bool {
        ProcessInfo.processInfo.environment["ANSIGHT_CAPTURE_UPLOAD_URL"] != nil
    }

    func configureOfflineUploadHarness() throws {
        let environment = ProcessInfo.processInfo.environment
        guard let endpoint = environment["ANSIGHT_CAPTURE_UPLOAD_URL"],
              let url = URL(string: endpoint), let key = environment["ANSIGHT_CAPTURE_UPLOAD_KEY"] else {
            throw OfflineCaptureError.invalidOptions
        }
        let configuration = try OfflineCaptureUploadConfiguration(endpoint: url, apiKey: key)
        cloudUploader = try AnsightRuntime.shared.makeOfflineCaptureUploader(configuration: configuration)
        offlineUploadMessage = "Recording WebP screenshots in 10-second chunks."
        let duration = Int(environment["ANSIGHT_CAPTURE_TEST_DURATION_SECONDS"] ?? "") ?? 0
        offlineUploadTask = Task { [weak self] in
            let started = Date()
            var stopped = false
            while !Task.isCancelled {
                guard let self else { return }
                if duration > 0 && !stopped && Date().timeIntervalSince(started) >= Double(duration) {
                    AnsightRuntime.shared.deactivate()
                    stopped = true
                }
                let uploaded = await self.uploadOfflineChunks()
                let finished = stopped
                    && uploaded
                    && self.offlinePendingChunks == 0
                    && !self.offlineCloudSessionIds.isEmpty
                if finished {
                    self.offlineUploadMessage = "Capture complete. All chunks acknowledged."
                    return
                }
                do { try await Task.sleep(nanoseconds: 3_000_000_000) }
                catch { return }
            }
        }
    }

    @discardableResult
    func uploadOfflineChunks() async -> Bool {
        guard let cloudUploader else { return false }
        do {
            let result = try await cloudUploader.uploadPending()
            offlineCloudSessionIds = Array(Set(offlineCloudSessionIds + result.sessionIds)).sorted()
            offlinePendingChunks = result.pendingChunks
            offlineUploadMessage = "\(result.acknowledgedChunks) acknowledged; \(result.pendingChunks) processing."
            if let failure = AnsightRuntime.shared.offlineCaptureLastError {
                offlineUploadMessage = failure
            }
        } catch {
            offlineUploadMessage = error.localizedDescription
            return false
        }
        return true
    }

    func stopOfflineCapture() {
        AnsightRuntime.shared.deactivate()
        offlineUploadMessage = "Recording stopped. Uploading the final chunk."
    }
}
