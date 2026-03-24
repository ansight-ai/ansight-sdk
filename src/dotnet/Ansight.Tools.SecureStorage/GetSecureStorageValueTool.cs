namespace Ansight.Tools.SecureStorage;

public sealed class GetSecureStorageValueTool : ITool
{
    private readonly SecureStorageToolsOptions options;

    public GetSecureStorageValueTool(SecureStorageToolsOptions? options = null)
    {
        this.options = options ?? SecureStorageToolsOptions.Default;
    }

    public string Category => "secure";

    public ToolScope Scope => ToolScope.Read;

    public string Id => SecureStorageToolIds.GetValue;

    public string Name => "Get Secure Storage Value";

    public string Description => "Reads a decrypted value from the configured secure storage backend.";

    public string Keywords => "secure storage keychain keystore encrypted get";

    public ToolSchema ArgumentsSchema => SecureStorageToolSchemas.GetValueArguments;

    public ToolSchema ResultSchema => SecureStorageToolSchemas.GetValueResult;

    public ToolSecurity Security => SecureStorageToolSecurityProfiles.GetValue;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(SecureStorageSupport.GetValue(options, arguments)));
        }
        catch (PlatformNotSupportedException exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "secure_platform_unsupported"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "secure_get_failed"));
        }
    }
}
