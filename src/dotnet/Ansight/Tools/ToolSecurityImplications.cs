namespace Ansight.Tools;

/// <summary>
/// Defines canonical implication identifiers that describe the security-sensitive behavior of a tool.
/// </summary>
public static class ToolSecurityImplications
{
    /// <summary>
    /// Indicates that the tool can disclose structural or descriptive metadata about the app or runtime.
    /// </summary>
    public const string MetadataDisclosure = "metadata_disclosure";

    /// <summary>
    /// Indicates that the tool can read application-owned data.
    /// </summary>
    public const string ReadsAppData = "reads_app_data";

    /// <summary>
    /// Indicates that the tool can create or modify application-owned data.
    /// </summary>
    public const string WritesAppData = "writes_app_data";

    /// <summary>
    /// Indicates that the tool can delete application-owned data.
    /// </summary>
    public const string DeletesAppData = "deletes_app_data";

    /// <summary>
    /// Indicates that the tool can export data outside the app boundary.
    /// </summary>
    public const string ExportsData = "exports_data";

    /// <summary>
    /// Indicates that the tool can access the app's file system or sandboxed files.
    /// </summary>
    public const string AccessesFileSystem = "accesses_file_system";

    /// <summary>
    /// Indicates that the tool can access database storage used by the app.
    /// </summary>
    public const string AccessesDatabases = "accesses_databases";

    /// <summary>
    /// Indicates that the tool can access app preferences or settings stores.
    /// </summary>
    public const string AccessesPreferences = "accesses_preferences";

    /// <summary>
    /// Indicates that the tool can access protected or secure storage mechanisms.
    /// </summary>
    public const string AccessesSecureStorage = "accesses_secure_storage";

    /// <summary>
    /// Indicates that the tool can handle secrets such as tokens, credentials, or keys.
    /// </summary>
    public const string HandlesSecrets = "handles_secrets";

    /// <summary>
    /// Indicates that the tool can inspect the app's UI hierarchy or rendered interface metadata.
    /// </summary>
    public const string InspectsUi = "inspects_ui";

    /// <summary>
    /// Indicates that the tool can capture screenshots of the app UI.
    /// </summary>
    public const string CapturesScreenshots = "captures_screenshots";

    /// <summary>
    /// Indicates that the tool can transfer or emit binary payloads.
    /// </summary>
    public const string UsesBinaryTransfer = "uses_binary_transfer";
}
