import AnsightCore
import Foundation

public enum AnsightFileDescriptorDiagnosticsToolSecurityProfiles {
    public static let countOpen = AnsightToolSecurity(
        level: .moderate,
        summary: "Reports process-level file descriptor usage.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.inspectsRuntimeState,
        ]
    )

    public static let listOpen = AnsightToolSecurity(
        level: .high,
        summary: "Lists live process descriptors and may disclose file paths or socket identifiers.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.inspectsRuntimeState,
            AnsightToolSecurityImplications.accessesFileSystem,
        ]
    )

    public static let inspect = listOpen

    public static let getUsage = AnsightToolSecurity(
        level: .moderate,
        summary: "Reports file descriptor limits and current process utilization.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.inspectsRuntimeState,
        ]
    )
}
