import AnsightCore
import Foundation

public enum AnsightPreferencesToolSecurityProfiles {
    public static let listKeys = AnsightToolSecurity(
        level: .moderate,
        summary: "Reveals preference key names and store metadata.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.accessesPreferences,
        ]
    )

    public static let getValue = AnsightToolSecurity(
        level: .high,
        summary: "Reads preference values that may include app configuration or user state.",
        implications: [
            AnsightToolSecurityImplications.readsAppData,
            AnsightToolSecurityImplications.accessesPreferences,
        ]
    )

    public static let setValue = AnsightToolSecurity(
        level: .high,
        summary: "Mutates preference values and can change app configuration or persisted state.",
        implications: [
            AnsightToolSecurityImplications.writesAppData,
            AnsightToolSecurityImplications.accessesPreferences,
        ]
    )

    public static let removeKey = AnsightToolSecurity(
        level: .high,
        summary: "Deletes persisted preference values and can remove app configuration or state.",
        implications: [
            AnsightToolSecurityImplications.deletesAppData,
            AnsightToolSecurityImplications.accessesPreferences,
        ]
    )
}
