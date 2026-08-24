namespace Ansight.Tools.SecureStorage;

public sealed class SetSecureStorageValueTool : ITool
{
    private readonly SecureStorageToolsOptions options;

    public SetSecureStorageValueTool(SecureStorageToolsOptions? options = null)
    {
        this.options = options ?? SecureStorageToolsOptions.Default;
    }

    public string Category => "secure";

    public ToolPolicy Policy => ToolPolicy.Critical;

    public string Id => SecureStorageToolIds.SetValue;

    public string Name => "Set Secure Storage Value";

    public string Description => "Writes a value into the configured secure storage backend.";

    public string Keywords => "secure storage keychain keystore encrypted write";

    public ToolSchema ArgumentsSchema => SecureStorageToolSchemas.SetValueArguments;

    public ToolSchema ResultSchema => SecureStorageToolSchemas.SetValueResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(SecureStorageSupport.SetValue(options, arguments)));
        }
        catch (PlatformNotSupportedException exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "secure_platform_unsupported"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "secure_set_failed"));
        }
    }
}
