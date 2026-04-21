namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
#endif

public sealed class SetAppThemeTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Write;

    public string Id => MauiToolIds.SetAppTheme;

    public string Name => "Set App Theme";

    public string Description => "Changes the live .NET MAUI application theme override.";

    public string Keywords => "maui app theme light dark system userapptheme requestedtheme";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.SetAppThemeArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.AppThemeResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.SetAppTheme;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var theme = GetRequiredString(arguments, "theme");
            var normalizedTheme = NormalizeThemeArgument(theme, out var appTheme);
            if (normalizedTheme == null)
            {
                return ToolResult.Failure("The theme argument must be one of: system, light, dark.", errorCode: "maui_invalid_app_theme");
            }

            var application = Application.Current;
            if (application == null)
            {
                return ToolResult.Failure("No MAUI application is available.", errorCode: "maui_application_unavailable");
            }

            var previousUserAppTheme = application.UserAppTheme;
            var previousRequestedTheme = application.RequestedTheme;

            application.UserAppTheme = appTheme;

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["updated"] = true,
                ["theme"] = normalizedTheme,
                ["previousUserAppTheme"] = previousUserAppTheme.ToString(),
                ["previousRequestedTheme"] = previousRequestedTheme.ToString(),
                ["userAppTheme"] = application.UserAppTheme.ToString(),
                ["requestedTheme"] = application.RequestedTheme.ToString()
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }

#if ANDROID || IOS || MACCATALYST
    private static string? NormalizeThemeArgument(string theme, out AppTheme appTheme)
    {
        if (string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase))
        {
            appTheme = AppTheme.Light;
            return "light";
        }

        if (string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase))
        {
            appTheme = AppTheme.Dark;
            return "dark";
        }

        if (string.Equals(theme, "system", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(theme, "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(theme, "device", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(theme, "unspecified", StringComparison.OrdinalIgnoreCase))
        {
            appTheme = AppTheme.Unspecified;
            return "system";
        }

        appTheme = AppTheme.Unspecified;
        return null;
    }
#endif
}
