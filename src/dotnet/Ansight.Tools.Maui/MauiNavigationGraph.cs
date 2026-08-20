namespace Ansight.Tools.Maui;

internal static class MauiNavigationGraph
{
    internal static TPage? ResolveDisplayedPage<TPage>(
        TPage? rootPage,
        Func<TPage, string> getId,
        Func<TPage, IReadOnlyList<TPage>> getModalStack,
        Func<TPage, TPage?> getDisplayedChild)
        where TPage : class
    {
        return ResolveDisplayedPage(
            rootPage,
            getId,
            getModalStack,
            getDisplayedChild,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    internal static TPage? ResolveDisplayedPage<TPage>(
        TPage? page,
        Func<TPage, string> getId,
        Func<TPage, IReadOnlyList<TPage>> getModalStack,
        Func<TPage, TPage?> getDisplayedChild,
        HashSet<string> visited)
        where TPage : class
    {
        if (page is null || !visited.Add(getId(page)))
        {
            return page;
        }

        var modalPage = FindTopModalPage(
            page,
            getId,
            getModalStack,
            getDisplayedChild,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (modalPage is not null && !ReferenceEquals(modalPage, page))
        {
            return ResolveDisplayedPage(
                modalPage,
                getId,
                getModalStack,
                getDisplayedChild,
                visited);
        }

        var childPage = getDisplayedChild(page);
        return childPage is null
            ? page
            : ResolveDisplayedPage(
                childPage,
                getId,
                getModalStack,
                getDisplayedChild,
                visited);
    }

    internal static TPage? FindTopModalPage<TPage>(
        TPage page,
        Func<TPage, string> getId,
        Func<TPage, IReadOnlyList<TPage>> getModalStack,
        Func<TPage, TPage?> getDisplayedChild,
        HashSet<string> visited)
        where TPage : class
    {
        if (!visited.Add(getId(page)))
        {
            return null;
        }

        var modalPage = getModalStack(page).LastOrDefault(candidate =>
            !ReferenceEquals(candidate, page)
            && !IsOnDisplayedNavigationPath(candidate, page, getId, getDisplayedChild));
        if (modalPage is not null)
        {
            return modalPage;
        }

        var childPage = getDisplayedChild(page);
        return childPage is null
            ? null
            : FindTopModalPage(
                childPage,
                getId,
                getModalStack,
                getDisplayedChild,
                visited);
    }

    internal static bool IsOnDisplayedNavigationPath<TPage>(
        TPage rootPage,
        TPage candidate,
        Func<TPage, string> getId,
        Func<TPage, TPage?> getDisplayedChild)
        where TPage : class
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TPage? currentPage = rootPage;
        while (currentPage is not null && visited.Add(getId(currentPage)))
        {
            if (ReferenceEquals(currentPage, candidate))
            {
                return true;
            }

            currentPage = getDisplayedChild(currentPage);
        }

        return false;
    }
}
