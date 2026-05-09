namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class GetCurrentPageTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Read;

    public string Id => MauiToolIds.GetCurrentPage;

    public string Name => "Get Current Page";

    public string Description => "Returns the currently displayed .NET MAUI page and navigation metadata.";

    public string Keywords => "maui page navigation current displayed shell";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.GetCurrentPageArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.CurrentPageResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.GetCurrentPage;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var application = Application.Current;
            if (application == null)
            {
                return ToolResult.Failure("No MAUI application is available.", errorCode: "maui_application_unavailable");
            }

            var window = GetActiveWindow(application);
            if (window == null)
            {
                return ToolResult.Failure("No active MAUI window is available.", errorCode: "maui_window_unavailable");
            }

            if (window.Page == null)
            {
                return ToolResult.Failure("The active MAUI window does not have a root page.", errorCode: "maui_page_unavailable");
            }

            var currentPage = ResolveDisplayedPage(window.Page) ?? window.Page;
            var activeNavigationPage = ResolveActiveNavigationPage(window.Page);
            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["windowCount"] = application.Windows.Count,
                ["window"] = CreateElementReference(window),
                ["rootPage"] = CreateElementReference(window.Page),
                ["currentPage"] = CreateElementReference(currentPage),
                ["activeNavigationPage"] = activeNavigationPage == null ? null : CreateElementReference(activeNavigationPage),
                ["navigation"] = CreateNavigationSnapshot(window.Page, currentPage)
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
