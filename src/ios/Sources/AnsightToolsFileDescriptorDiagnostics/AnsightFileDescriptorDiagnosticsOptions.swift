import Foundation

public struct AnsightFileDescriptorDiagnosticsOptions: Sendable, Codable, Equatable {
    public static let `default` = AnsightFileDescriptorDiagnosticsOptions()

    public let includeTargets: Bool
    public let maximumScannedDescriptors: Int
    public let maximumReturnedDescriptors: Int

    public init(
        includeTargets: Bool = true,
        maximumScannedDescriptors: Int = 1_048_576,
        maximumReturnedDescriptors: Int = 2_048
    ) {
        self.includeTargets = includeTargets
        self.maximumScannedDescriptors = min(max(maximumScannedDescriptors, 1), 1_048_576)
        self.maximumReturnedDescriptors = min(max(maximumReturnedDescriptors, 1), 8_192)
    }
}
