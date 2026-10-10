import Foundation

struct OfflineCaptureTransferTool: AnsightTool {
    let runtime: AnsightRuntime
    let descriptor = AnsightToolDescriptor(id: "offline_capture.transfer", name: "Transfer offline captures",
        description: "Import retained offline sessions into this host, then purge acknowledged local copies.",
        category: "Diagnostics", policy: .write)
    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        let result = runtime.transferOfflineCaptures()
        return result.success ? .success(message: result.message) : .failure(result.message)
    }
}
