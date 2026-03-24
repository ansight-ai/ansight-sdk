namespace Ansight.Tools;

public static class ToolSecurityImplications
{
    public const string MetadataDisclosure = "metadata_disclosure";
    public const string ReadsAppData = "reads_app_data";
    public const string WritesAppData = "writes_app_data";
    public const string DeletesAppData = "deletes_app_data";
    public const string ExportsData = "exports_data";
    public const string AccessesFileSystem = "accesses_file_system";
    public const string AccessesDatabases = "accesses_databases";
    public const string AccessesPreferences = "accesses_preferences";
    public const string AccessesSecureStorage = "accesses_secure_storage";
    public const string HandlesSecrets = "handles_secrets";
    public const string InspectsUi = "inspects_ui";
    public const string CapturesScreenshots = "captures_screenshots";
    public const string UsesBinaryTransfer = "uses_binary_transfer";
}
