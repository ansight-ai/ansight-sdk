namespace Ansight.Tools.Preferences;

public sealed class ListPreferenceKeysTool : ITool
{
    private readonly PreferencesToolsOptions options;

    public ListPreferenceKeysTool(PreferencesToolsOptions? options = null)
    {
        this.options = options ?? PreferencesToolsOptions.Default;
    }

    public string Category => "prefs";

    public ToolScope Scope => ToolScope.Read;

    public string Id => "prefs.list_keys";

    public string Name => "List Preference Keys";

    public string Description => "Lists keys from a shared preferences or user defaults store.";

    public string Keywords => "preferences sharedpreferences nsuserdefaults storage keys";

    public ToolSchema ArgumentsSchema => PreferencesToolSchemas.ListKeysArguments;

    public ToolSchema ResultSchema => PreferencesToolSchemas.ListKeysResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(PreferencesSupport.ListKeys(options, arguments)));
        }
        catch (PlatformNotSupportedException exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "prefs_platform_unsupported"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "prefs_list_failed"));
        }
    }
}
