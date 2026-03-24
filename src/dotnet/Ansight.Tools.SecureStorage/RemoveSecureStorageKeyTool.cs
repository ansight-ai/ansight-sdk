namespace Ansight.Tools.SecureStorage;

public sealed class RemoveSecureStorageKeyTool : ITool
{
    private readonly SecureStorageToolsOptions options;

    public RemoveSecureStorageKeyTool(SecureStorageToolsOptions? options = null)
    {
        this.options = options ?? SecureStorageToolsOptions.Default;
    }

    public string Category => "secure";

    public ToolScope Scope => ToolScope.Delete;

    public string Id => SecureStorageToolIds.RemoveKey;

    public string Name => "Remove Secure Storage Key";

    public string Description => "Deletes a value from the configured secure storage backend.";

    public string Keywords => "secure storage keychain keystore encrypted delete remove";

    public ToolSchema ArgumentsSchema => SecureStorageToolSchemas.RemoveKeyArguments;

    public ToolSchema ResultSchema => SecureStorageToolSchemas.RemoveKeyResult;

    public ToolSecurity Security => SecureStorageToolSecurityProfiles.RemoveKey;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(SecureStorageSupport.RemoveKey(options, arguments)));
        }
        catch (PlatformNotSupportedException exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "secure_platform_unsupported"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "secure_remove_failed"));
        }
    }
}
