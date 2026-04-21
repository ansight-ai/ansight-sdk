namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class GetNavigationStateTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Read;

    public string Id => MauiToolIds.GetNavigationState;

    public string Name => "Get Navigation State";

    public string Description => "Returns active window, page, navigation stack, modal stack, and Shell navigation metadata.";

    public string Keywords => "maui navigation shell route modal stack tabs flyout current page";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.GetNavigationStateArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.NavigationStateResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.GetNavigationState;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var includeWindows = GetBoolean(arguments, "includeWindows", defaultValue: true);
            var includeShellItems = GetBoolean(arguments, "includeShellItems", defaultValue: true);

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
            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["windowCount"] = application.Windows.Count,
                ["activeWindow"] = CreateElementReference(window),
                ["rootPage"] = CreateElementReference(window.Page),
                ["currentPage"] = CreateElementReference(currentPage),
                ["navigation"] = CreateNavigationSnapshot(window.Page, currentPage)
            };

            if (includeWindows)
            {
                var windows = new JsonArray();
                foreach (var appWindow in application.Windows)
                {
                    var windowJson = CreateElementReference(appWindow);
                    windowJson["hasHandler"] = appWindow.Handler != null;
                    windowJson["page"] = appWindow.Page == null ? null : CreateElementReference(appWindow.Page);
                    windows.Add(windowJson);
                }

                payload["windows"] = windows;
            }

            if (includeShellItems && window.Page is Shell shell)
            {
                var shellJson = new JsonObject
                {
                    ["currentState"] = CreateSafeNavigationLocation(shell.CurrentState?.Location?.ToString()),
                    ["currentItem"] = shell.CurrentItem == null ? null : CreateElementReference(shell.CurrentItem),
                    ["currentSection"] = shell.CurrentItem?.CurrentItem == null ? null : CreateElementReference(shell.CurrentItem.CurrentItem),
                    ["currentContent"] = shell.CurrentItem?.CurrentItem?.CurrentItem == null ? null : CreateElementReference(shell.CurrentItem.CurrentItem.CurrentItem),
                    ["currentPage"] = shell.CurrentPage == null ? null : CreateElementReference(shell.CurrentPage)
                };

                var items = new JsonArray();
                foreach (var item in shell.Items)
                {
                    var itemJson = CreateElementReference(item);
                    itemJson["isCurrent"] = ReferenceEquals(item, shell.CurrentItem);

                    var sections = new JsonArray();
                    foreach (var section in item.Items)
                    {
                        var sectionJson = CreateElementReference(section);
                        sectionJson["isCurrent"] = ReferenceEquals(section, item.CurrentItem);

                        var contents = new JsonArray();
                        foreach (var content in section.Items)
                        {
                            var contentJson = CreateElementReference(content);
                            contentJson["isCurrent"] = ReferenceEquals(content, section.CurrentItem);
                            contents.Add(contentJson);
                        }

                        sectionJson["contents"] = contents;
                        sections.Add(sectionJson);
                    }

                    itemJson["sections"] = sections;
                    items.Add(itemJson);
                }

                shellJson["items"] = items;
                payload["shell"] = shellJson;
            }

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
