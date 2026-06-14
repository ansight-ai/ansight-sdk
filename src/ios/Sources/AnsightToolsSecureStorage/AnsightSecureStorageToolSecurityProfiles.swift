import AnsightKit
import Foundation

public enum AnsightSecureStorageToolSecurityProfiles {
    public static let getValue = AnsightToolSecurity(
        level: .critical,
        summary: "Reads decrypted secure-storage values that may contain credentials or tokens.",
        implications: [
            AnsightToolSecurityImplications.readsAppData,
            AnsightToolSecurityImplications.exportsData,
            AnsightToolSecurityImplications.accessesSecureStorage,
            AnsightToolSecurityImplications.handlesSecrets,
        ]
    )

    public static let setValue = AnsightToolSecurity(
        level: .critical,
        summary: "Writes secret material into secure storage and can change authentication state.",
        implications: [
            AnsightToolSecurityImplications.writesAppData,
            AnsightToolSecurityImplications.accessesSecureStorage,
            AnsightToolSecurityImplications.handlesSecrets,
        ]
    )

    public static let removeKey = AnsightToolSecurity(
        level: .critical,
        summary: "Deletes secret material from secure storage and can invalidate authentication state.",
        implications: [
            AnsightToolSecurityImplications.deletesAppData,
            AnsightToolSecurityImplications.accessesSecureStorage,
            AnsightToolSecurityImplications.handlesSecrets,
        ]
    )
}
