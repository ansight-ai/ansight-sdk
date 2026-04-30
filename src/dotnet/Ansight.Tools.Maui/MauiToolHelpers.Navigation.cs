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
        rootElement = null!;
        error = null;
        normalizedRootScope = NormalizeRootScope(rootScope);

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

        switch (normalizedRootScope)
        {
            case "window":
                rootElement = window;
                return true;
            case "rootPage":
                if (window.Page == null)
                {
                    error = "The active MAUI window does not have a root page.";
                    return false;
                }

                rootElement = window.Page;
                return true;
            case "currentPage":
                if (window.Page == null)
                {
                    error = "The active MAUI window does not have a root page.";
                    return false;
                }

                rootElement = ResolveDisplayedPage(window.Page) ?? window.Page;
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
        return ResolveDisplayedPage(rootPage, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    internal static Page? ResolveDisplayedPage(Page? page, HashSet<string> visited)
    {
        if (page == null || !visited.Add(GetElementId(page)))
        {
            return page;
        }

        var modalPage = FindTopModalPage(page, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (modalPage != null && !ReferenceEquals(modalPage, page))
        {
            return ResolveDisplayedPage(modalPage, visited);
        }

        var childPage = GetDisplayedNavigationChildPage(page);
        if (childPage != null)
        {
            return ResolveDisplayedPage(childPage, visited);
        }

        return page;
    }

    private static Page? FindTopModalPage(Page page, HashSet<string> visited)
    {
        if (!visited.Add(GetElementId(page)))
        {
            return null;
        }

        var modalPage = GetModalStack(page).LastOrDefault(modal => !ReferenceEquals(modal, page));
        if (modalPage != null)
        {
            return modalPage;
        }

        var childPage = GetDisplayedNavigationChildPage(page);
        return childPage == null ? null : FindTopModalPage(childPage, visited);
    }

    private static Page? GetDisplayedNavigationChildPage(Page page)
    {
        return page switch
        {
            Shell shell when shell.CurrentPage != null => shell.CurrentPage,
            NavigationPage navigationPage when navigationPage.CurrentPage != null => navigationPage.CurrentPage,
            TabbedPage tabbedPage when tabbedPage.CurrentPage != null => tabbedPage.CurrentPage,
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
        var navigation = new JsonObject
        {
            ["rootPageId"] = GetElementId(rootPage),
            ["currentPageId"] = GetElementId(currentPage)
        };

        var navigationStack = new JsonArray();
        if (currentPage.Navigation?.NavigationStack != null)
        {
            foreach (var page in currentPage.Navigation.NavigationStack)
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

        return navigation;
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
