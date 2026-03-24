namespace Ansight.Tools.Preferences;

public sealed class GetPreferenceValueTool : ITool
{
    private readonly PreferencesToolsOptions options;

    public GetPreferenceValueTool(PreferencesToolsOptions? options = null)
    {
        this.options = options ?? PreferencesToolsOptions.Default;
    }

    public string Category => "prefs";

    public ToolScope Scope => ToolScope.Read;

    public string Id => "prefs.get_value";

    public string Name => "Get Preference Value";

    public string Description => "Reads a value from shared preferences or user defaults.";

    public string Keywords => "preferences sharedpreferences nsuserdefaults storage get";

    public ToolSchema ArgumentsSchema => PreferencesToolSchemas.GetValueArguments;

    public ToolSchema ResultSchema => PreferencesToolSchemas.GetValueResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(PreferencesSupport.GetValue(options, arguments)));
        }
        catch (PlatformNotSupportedException exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "prefs_platform_unsupported"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "prefs_get_failed"));
        }
    }
}
