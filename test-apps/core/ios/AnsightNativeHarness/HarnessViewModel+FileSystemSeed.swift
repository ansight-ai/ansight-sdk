import Foundation

extension HarnessViewModel {
    func prepareHarnessFileSystemSample() throws {
        guard let directory = harnessDirectoryURL() else {
            throw harnessError("Unable to resolve the app Documents directory.")
        }

        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let file = directory.appendingPathComponent("hello.txt")
        let contents = """
        Ansight Native Harness file-system sample.
        Seeded at \(seededAtUtc).
        Use this file to validate iOS SDK file tools from Ansight host.
        """
        try Data(contents.utf8).write(to: file, options: [.atomic])
    }
}
