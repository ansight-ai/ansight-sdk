namespace Ansight.Tools.Preferences;

public sealed class SetPreferenceValueTool : ITool
{
    private readonly PreferencesToolsOptions options;

    public SetPreferenceValueTool(PreferencesToolsOptions? options = null)
    {
        this.options = options ?? PreferencesToolsOptions.Default;
    }

    public string Category => "prefs";

    public ToolScope Scope => ToolScope.Write;

    public string Id => "prefs.set_value";

    public string Name => "Set Preference Value";

    public string Description => "Writes a value into shared preferences or user defaults.";

    public string Keywords => "preferences sharedpreferences nsuserdefaults storage write";

    public ToolSchema ArgumentsSchema => PreferencesToolSchemas.SetValueArguments;

    public ToolSchema ResultSchema => PreferencesToolSchemas.SetValueResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(PreferencesSupport.SetValue(options, arguments)));
        }
        catch (PlatformNotSupportedException exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "prefs_platform_unsupported"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "prefs_set_failed"));
        }
    }
}
