namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls;

internal static partial class MauiToolHelpers
{
    internal static Window? GetActiveWindow(Application application)
    {
        var windows = application.Windows;
        if (windows.Count == 0)
        {
            return null;
        }

        foreach (var window in windows.Reverse())
        {
            if (window.Page != null && window.Handler != null)
            {
                return window;
            }
        }

        return windows.Reverse().FirstOrDefault(window => window.Page != null)
            ?? windows[^1];
    }

    internal static bool TryGetActiveRoot(string rootScope, out Element rootElement, out string? error, out string normalizedRootScope)
    {
        if (TryGetActiveRootContext(rootScope, out var context, out error))
        {
            rootElement = context.Root;
            normalizedRootScope = context.NormalizedRootScope;
            return true;
        }

        rootElement = null!;
        normalizedRootScope = NormalizeRootScope(rootScope);
        return false;
    }

    internal static bool TryGetActiveRootContext(string rootScope, out MauiActiveRootContext context, out string? error)
    {
        context = null!;
        error = null;
        var normalizedRootScope = NormalizeRootScope(rootScope);

        var application = Application.Current;
        if (application == null)
        {
            error = "No MAUI application is available.";
            return false;
        }

        var window = GetActiveWindow(application);
        if (window == null)
        {
            error = "No active MAUI window is available.";
            return false;
        }

        var rootPage = window.Page;
        var currentPage = rootPage == null ? null : ResolveDisplayedPage(rootPage) ?? rootPage;
        var activeNavigationPage = rootPage == null ? null : ResolveActiveNavigationPage(rootPage);

        switch (normalizedRootScope)
        {
            case "window":
                context = new MauiActiveRootContext(window, rootPage, currentPage, activeNavigationPage, window, normalizedRootScope);
                return true;
            case "rootPage":
                if (rootPage == null)
                {
                    error = "The active MAUI window does not have a root page.";
                    return false;
                }

                context = new MauiActiveRootContext(window, rootPage, currentPage, activeNavigationPage, rootPage, normalizedRootScope);
                return true;
            case "currentPage":
                if (rootPage == null)
                {
                    error = "The active MAUI window does not have a root page.";
                    return false;
                }

                context = new MauiActiveRootContext(window, rootPage, currentPage, activeNavigationPage, currentPage ?? rootPage, normalizedRootScope);
                return true;
            default:
                error = "The root argument must be one of: currentPage, rootPage, window.";
                return false;
        }
    }

    internal static string NormalizeRootScope(string rootScope)
    {
        if (string.Equals(rootScope, "window", StringComparison.OrdinalIgnoreCase))
        {
            return "window";
        }

        if (string.Equals(rootScope, "rootPage", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootScope, "root_page", StringComparison.OrdinalIgnoreCase))
        {
            return "rootPage";
        }

        if (string.Equals(rootScope, "currentPage", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootScope, "current_page", StringComparison.OrdinalIgnoreCase))
        {
            return "currentPage";
        }

        return rootScope;
    }

    internal static Page? ResolveDisplayedPage(Page? rootPage)
    {
        return MauiNavigationGraph.ResolveDisplayedPage(
            rootPage,
            GetElementId,
            GetModalStack,
            GetDisplayedNavigationChildPage);
    }

    internal static Page? ResolveDisplayedPage(Page? page, HashSet<string> visited)
    {
        return MauiNavigationGraph.ResolveDisplayedPage(
            page,
            GetElementId,
            GetModalStack,
            GetDisplayedNavigationChildPage,
            visited);
    }

    internal static NavigationPage? ResolveActiveNavigationPage(Page? rootPage)
    {
        return ResolveActiveNavigationPage(rootPage, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static NavigationPage? ResolveActiveNavigationPage(Page? page, HashSet<string> visited)
    {
        if (page == null || !visited.Add(GetElementId(page)))
        {
            return null;
        }

        var modalPage = FindTopModalPage(page, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (modalPage != null && !ReferenceEquals(modalPage, page))
        {
            return ResolveActiveNavigationPage(modalPage, visited);
        }

        if (page is NavigationPage navigationPage)
        {
            return navigationPage;
        }

        var childPage = GetDisplayedNavigationChildPage(page);
        return childPage == null ? null : ResolveActiveNavigationPage(childPage, visited);
    }

    private static Page? FindTopModalPage(Page page, HashSet<string> visited)
    {
        return MauiNavigationGraph.FindTopModalPage(
            page,
            GetElementId,
            GetModalStack,
            GetDisplayedNavigationChildPage,
            visited);
    }

    private static Page? GetDisplayedNavigationChildPage(Page page)
    {
        return page switch
        {
            Shell shell when shell.CurrentPage != null => shell.CurrentPage,
            NavigationPage navigationPage when navigationPage.CurrentPage != null => navigationPage.CurrentPage,
            TabbedPage tabbedPage when tabbedPage.CurrentPage != null => tabbedPage.CurrentPage,
            FlyoutPage { IsPresented: true, Flyout: not null } flyoutPage => flyoutPage.Flyout,
            FlyoutPage flyoutPage when flyoutPage.Detail != null => flyoutPage.Detail,
            _ => null
        };
    }

    private static IReadOnlyList<Page> GetModalStack(Page page)
    {
        try
        {
            return page.Navigation?.ModalStack ?? Array.Empty<Page>();
        }
        catch
        {
            return Array.Empty<Page>();
        }
    }

    internal static MauiElementResolution? ResolveElement(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        var application = Application.Current;
        var window = application == null ? null : GetActiveWindow(application);
        if (window == null)
        {
            return null;
        }

        var roots = new List<Element> { window };
        if (window.Page != null && !ReferenceEquals(window.Page, window))
        {
            roots.Add(window.Page);

            var displayedPage = ResolveDisplayedPage(window.Page);
            if (displayedPage != null && !roots.Any(root => ReferenceEquals(root, displayedPage)))
            {
                roots.Add(displayedPage);
            }
        }

        foreach (var root in roots)
        {
            var ancestors = new List<Element>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (TryFindElement(root, nodeId, ancestors, visited, out var element) && element != null)
            {
                return new MauiElementResolution(root, element, ancestors.ToArray());
            }
        }

        return null;
    }

    internal static JsonObject CreateNavigationSnapshot(Page rootPage, Page currentPage)
    {
        var activeNavigationPage = ResolveActiveNavigationPage(rootPage);
        var navigation = new JsonObject
        {
            ["rootPageId"] = GetElementId(rootPage),
            ["currentPageId"] = GetElementId(currentPage),
            ["activeNavigationPageId"] = activeNavigationPage == null ? null : GetElementId(activeNavigationPage),
            ["activeNavigationPage"] = activeNavigationPage == null ? null : CreateElementReference(activeNavigationPage)
        };

        var navigationStack = new JsonArray();
        var navigationStackSource = activeNavigationPage?.Navigation?.NavigationStack ?? currentPage.Navigation?.NavigationStack;
        if (navigationStackSource != null)
        {
            foreach (var page in navigationStackSource)
            {
                navigationStack.Add(CreateElementReference(page));
            }
        }

        var modalStack = new JsonArray();
        foreach (var page in ResolveModalStack(rootPage, currentPage))
        {
            modalStack.Add(CreateElementReference(page));
        }

        navigation["navigationStack"] = navigationStack;
        navigation["modalStack"] = modalStack;

        if (rootPage is Shell shell)
        {
            navigation["shellCurrentState"] = CreateSafeNavigationLocation(shell.CurrentState?.Location?.ToString());
            navigation["shellCurrentItem"] = shell.CurrentItem == null ? null : CreateElementReference(shell.CurrentItem);
            navigation["shellCurrentPage"] = shell.CurrentPage == null ? null : CreateElementReference(shell.CurrentPage);
        }

        var activeFlyoutPage = ResolveActiveFlyoutPage(rootPage);
        if (activeFlyoutPage != null)
        {
            navigation["flyout"] = CreateFlyoutSnapshot(activeFlyoutPage);
        }

        var activeTabbedPage = ResolveActiveTabbedPage(rootPage);
        if (activeTabbedPage != null)
        {
            navigation["tabbed"] = CreateTabbedSnapshot(activeTabbedPage);
        }

        return navigation;
    }

    private static FlyoutPage? ResolveActiveFlyoutPage(Page? page)
    {
        return ResolveActiveFlyoutPage(page, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static FlyoutPage? ResolveActiveFlyoutPage(Page? page, HashSet<string> visited)
    {
        if (page == null || !visited.Add(GetElementId(page)))
        {
            return null;
        }

        var modalPage = FindTopModalPage(page, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (modalPage != null && !ReferenceEquals(modalPage, page))
        {
            return ResolveActiveFlyoutPage(modalPage, visited);
        }

        if (page is FlyoutPage flyoutPage)
        {
            return flyoutPage;
        }

        var childPage = GetDisplayedNavigationChildPage(page);
        return childPage == null ? null : ResolveActiveFlyoutPage(childPage, visited);
    }

    private static TabbedPage? ResolveActiveTabbedPage(Page? page)
    {
        return ResolveActiveTabbedPage(page, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static TabbedPage? ResolveActiveTabbedPage(Page? page, HashSet<string> visited)
    {
        if (page == null || !visited.Add(GetElementId(page)))
        {
            return null;
        }

        var modalPage = FindTopModalPage(page, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (modalPage != null && !ReferenceEquals(modalPage, page))
        {
            return ResolveActiveTabbedPage(modalPage, visited);
        }

        if (page is TabbedPage tabbedPage)
        {
            return tabbedPage;
        }

        var childPage = GetDisplayedNavigationChildPage(page);
        return childPage == null ? null : ResolveActiveTabbedPage(childPage, visited);
    }

    private static JsonObject CreateFlyoutSnapshot(FlyoutPage flyoutPage)
    {
        var displayedPage = GetDisplayedNavigationChildPage(flyoutPage);
        return new JsonObject
        {
            ["page"] = CreateElementReference(flyoutPage),
            ["isPresented"] = flyoutPage.IsPresented,
            ["displayedPage"] = displayedPage == null ? null : CreateElementReference(displayedPage),
            ["flyoutPage"] = flyoutPage.Flyout == null ? null : CreateElementReference(flyoutPage.Flyout),
            ["detailPage"] = flyoutPage.Detail == null ? null : CreateElementReference(flyoutPage.Detail)
        };
    }

    private static JsonObject CreateTabbedSnapshot(TabbedPage tabbedPage)
    {
        var tabs = new JsonArray();
        var selectedIndex = -1;
        for (var index = 0; index < tabbedPage.Children.Count; index++)
        {
            var tab = tabbedPage.Children[index];
            var isCurrent = ReferenceEquals(tab, tabbedPage.CurrentPage);
            if (isCurrent)
            {
                selectedIndex = index;
            }

            var tabJson = CreateElementReference(tab);
            tabJson["index"] = index;
            tabJson["isCurrent"] = isCurrent;
            tabs.Add(tabJson);
        }

        return new JsonObject
        {
            ["page"] = CreateElementReference(tabbedPage),
            ["currentPage"] = tabbedPage.CurrentPage == null ? null : CreateElementReference(tabbedPage.CurrentPage),
            ["selectedIndex"] = selectedIndex,
            ["tabs"] = tabs
        };
    }

    private static IReadOnlyList<Page> ResolveModalStack(Page rootPage, Page currentPage)
    {
        var currentPageModalStack = GetModalStack(currentPage);
        if (currentPageModalStack.Count > 0)
        {
            return currentPageModalStack;
        }

        var rootPageModalStack = FindModalStack(rootPage, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return rootPageModalStack.Count > 0 ? rootPageModalStack : Array.Empty<Page>();
    }

    private static IReadOnlyList<Page> FindModalStack(Page page, HashSet<string> visited)
    {
        if (!visited.Add(GetElementId(page)))
        {
            return Array.Empty<Page>();
        }

        var modalStack = GetModalStack(page);
        if (modalStack.Count > 0)
        {
            return modalStack;
        }

        var childPage = GetDisplayedNavigationChildPage(page);
        return childPage == null ? Array.Empty<Page>() : FindModalStack(childPage, visited);
    }
}
#endif
