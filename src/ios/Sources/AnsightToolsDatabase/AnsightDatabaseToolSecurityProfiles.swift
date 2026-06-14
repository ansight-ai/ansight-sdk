import AnsightKit
import Foundation

public enum AnsightDatabaseToolSecurityProfiles {
    public static let listDatabases = AnsightToolSecurity(
        level: .moderate,
        summary: "Reveals database names and storage locations inside the app sandbox.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.accessesDatabases,
        ]
    )

    public static let describeSchema = AnsightToolSecurity(
        level: .moderate,
        summary: "Reveals database structure, including table and column metadata.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.accessesDatabases,
        ]
    )

    public static let query = AnsightToolSecurity(
        level: .high,
        summary: "Reads and exports structured data from an app database.",
        implications: [
            AnsightToolSecurityImplications.readsAppData,
            AnsightToolSecurityImplications.exportsData,
            AnsightToolSecurityImplications.accessesDatabases,
        ]
    )
}
