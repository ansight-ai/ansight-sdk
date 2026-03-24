namespace Ansight.Tools.SecureStorage;

public static class SecureStorageToolSecurityProfiles
{
    public static ToolSecurity GetValue { get; } = new(
        ToolSecurityLevel.Critical,
        "Reads decrypted secure-storage values that may contain credentials or tokens.",
        ToolSecurityImplications.ReadsAppData,
        ToolSecurityImplications.ExportsData,
        ToolSecurityImplications.AccessesSecureStorage,
        ToolSecurityImplications.HandlesSecrets);

    public static ToolSecurity SetValue { get; } = new(
        ToolSecurityLevel.Critical,
        "Writes secret material into secure storage and can change authentication state.",
        ToolSecurityImplications.WritesAppData,
        ToolSecurityImplications.AccessesSecureStorage,
        ToolSecurityImplications.HandlesSecrets);

    public static ToolSecurity RemoveKey { get; } = new(
        ToolSecurityLevel.Critical,
        "Deletes secret material from secure storage and can invalidate authentication state.",
        ToolSecurityImplications.DeletesAppData,
        ToolSecurityImplications.AccessesSecureStorage,
        ToolSecurityImplications.HandlesSecrets);
}
