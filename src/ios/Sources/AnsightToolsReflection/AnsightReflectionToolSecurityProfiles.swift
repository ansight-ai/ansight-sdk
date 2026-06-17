import AnsightCore
import Foundation

public enum AnsightReflectionToolSecurityProfiles {
    public static let listRoots = AnsightToolSecurity(
        level: .high,
        summary: "Reveals registered runtime object roots available for reflection.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.inspectsRuntimeState,
        ]
    )

    public static let inspectObject = AnsightToolSecurity(
        level: .critical,
        summary: "Reads live object values from registered reflection roots.",
        implications: [
            AnsightToolSecurityImplications.readsAppData,
            AnsightToolSecurityImplications.inspectsRuntimeState,
        ]
    )

    public static let describeType = AnsightToolSecurity(
        level: .moderate,
        summary: "Reveals runtime type metadata.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
        ]
    )

    public static let setMemberValue = AnsightToolSecurity(
        level: .critical,
        summary: "Mutates registered runtime object state through opt-in reflection roots.",
        implications: [
            AnsightToolSecurityImplications.writesAppData,
            AnsightToolSecurityImplications.mutatesRuntimeState,
        ]
    )

    public static let invokeMethod = AnsightToolSecurity(
        level: .critical,
        summary: "Invokes app code through opt-in reflection roots.",
        implications: [
            AnsightToolSecurityImplications.invokesAppCode,
            AnsightToolSecurityImplications.mutatesRuntimeState,
        ]
    )
}
