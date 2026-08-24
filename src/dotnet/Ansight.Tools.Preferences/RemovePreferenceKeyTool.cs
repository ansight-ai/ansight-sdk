namespace Ansight.Tools.Preferences;

public sealed class RemovePreferenceKeyTool : ITool
{
    private readonly PreferencesToolsOptions options;

    public RemovePreferenceKeyTool(PreferencesToolsOptions? options = null)
    {
        this.options = options ?? PreferencesToolsOptions.Default;
    }

    public string Category => "prefs";

    public ToolPolicy Policy => ToolPolicy.Critical;

    public string Id => PreferencesToolIds.RemoveKey;

    public string Name => "Remove Preference Key";

    public string Description => "Deletes a key from shared preferences or user defaults.";

    public string Keywords => "preferences sharedpreferences nsuserdefaults storage delete remove";

    public ToolSchema ArgumentsSchema => PreferencesToolSchemas.RemoveKeyArguments;

    public ToolSchema ResultSchema => PreferencesToolSchemas.RemoveKeyResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(PreferencesSupport.RemoveKey(options, arguments)));
        }
        catch (PlatformNotSupportedException exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "prefs_platform_unsupported"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "prefs_remove_failed"));
        }
    }
}
