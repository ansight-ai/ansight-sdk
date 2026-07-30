import Foundation

public enum AnsightDeveloperMode {
    public static var bundledToolScanReport: AnsightBundledToolScanReport {
        AnsightBundledToolScanReport(
            detectedToolTypes: providerType?.detectedBundledToolTypes() ?? [],
            allowBundledTools: providerType?.allowBundledTools() ?? false
        )
    }

    private static var providerType: AnsightBuildArtifactsProviding.Type? {
        resolveProviderType()
    }

    private static func resolveProviderType() -> AnsightBuildArtifactsProviding.Type? {
        if let direct = NSClassFromString("AnsightGeneratedBuildArtifactsProvider") as? AnsightBuildArtifactsProviding.Type {
            return direct
        }

        let probeClassName = NSStringFromClass(AnsightBuildArtifactsClassProbe.self)
        let components = probeClassName.split(separator: ".")
        guard components.count > 1 else {
            return nil
        }

        let moduleName = components.dropLast().joined(separator: ".")
        let qualifiedName = "\(moduleName).AnsightGeneratedBuildArtifactsProvider"
        return NSClassFromString(qualifiedName) as? AnsightBuildArtifactsProviding.Type
    }
}
