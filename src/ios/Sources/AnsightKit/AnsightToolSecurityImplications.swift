import Foundation

public enum AnsightToolSecurityImplications {
    public static let metadataDisclosure = "metadata_disclosure"
    public static let readsAppData = "reads_app_data"
    public static let writesAppData = "writes_app_data"
    public static let deletesAppData = "deletes_app_data"
    public static let exportsData = "exports_data"
    public static let accessesFileSystem = "accesses_file_system"
    public static let accessesDatabases = "accesses_databases"
    public static let accessesPreferences = "accesses_preferences"
    public static let accessesSecureStorage = "accesses_secure_storage"
    public static let handlesSecrets = "handles_secrets"
    public static let inspectsUi = "inspects_ui"
    public static let inspectsRuntimeState = "inspects_runtime_state"
    public static let mutatesRuntimeState = "mutates_runtime_state"
    public static let invokesAppCode = "invokes_app_code"
    public static let capturesScreenshots = "captures_screenshots"
    public static let usesBinaryTransfer = "uses_binary_transfer"
}
