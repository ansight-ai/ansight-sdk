import Foundation

extension HarnessViewModel {
    func databaseURL() -> URL? {
        harnessDirectoryURL()?.appendingPathComponent("sample.sqlite")
    }

    func harnessDirectoryURL() -> URL? {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)
            .first?
            .appendingPathComponent("ansight-harness", isDirectory: true)
    }

    func harnessError(_ message: String) -> NSError {
        NSError(
            domain: "AnsightNativeHarness",
            code: 1,
            userInfo: [NSLocalizedDescriptionKey: message]
        )
    }
}
