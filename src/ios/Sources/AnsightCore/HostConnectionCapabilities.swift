import Foundation

public struct HostConnectionCapabilities: Sendable, Codable, Equatable {
    public let canConnectUsingSavedConfig: Bool
    public let canConnectUsingBundledConfig: Bool
    public let canChooseConfigFile: Bool
    public let canScanConfigQrCode: Bool
    public let canClearSavedConfigs: Bool

    public init(
        canConnectUsingSavedConfig: Bool,
        canConnectUsingBundledConfig: Bool,
        canChooseConfigFile: Bool,
        canScanConfigQrCode: Bool,
        canClearSavedConfigs: Bool
    ) {
        self.canConnectUsingSavedConfig = canConnectUsingSavedConfig
        self.canConnectUsingBundledConfig = canConnectUsingBundledConfig
        self.canChooseConfigFile = canChooseConfigFile
        self.canScanConfigQrCode = canScanConfigQrCode
        self.canClearSavedConfigs = canClearSavedConfigs
    }
}
