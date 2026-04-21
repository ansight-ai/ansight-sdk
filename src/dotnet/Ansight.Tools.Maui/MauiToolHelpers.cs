namespace Ansight.Tools.Maui;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Xml;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
#endif

internal static class MauiToolHelpers
{
    internal const int DefaultTreeDepth = 8;
    internal const int MaximumTreeDepth = 64;
    internal const int DefaultObjectDepth = 1;
    internal const int MaximumObjectDepth = 4;
    internal const int DefaultMaxItems = 16;
    internal const int MaximumMaxItems = 64;
    internal const int DefaultMaxProperties = 32;
    internal const int MaximumMaxProperties = 128;
    internal const int DefaultSearchResults = 32;
    internal const int MaximumSearchResults = 256;
    internal const int MaximumStringLength = 512;
    internal const string RedactedLabel = "[redacted]";
    private static readonly string[] sensitiveLabelKeywords =
    [
        "access token",
        "account number",
        "api key",
        "apikey",
        "auth token",
        "authorization",
        "bearer",
        "card number",
        "credential",
        "credit card",
        "cvc",
        "cvv",
        "mfa",
        "one time",
        "otp",
        "passcode",
        "password",
        "private key",
        "refresh token",
        "routing number",
        "secret",
        "social security",
        "ssn",
        "token"
    ];
    internal static readonly JsonSerializerOptions jsonSerializerOptions = new(JsonSerializerDefaults.Web);

#if ANDROID || IOS || MACCATALYST
    private static readonly Dictionary<string, Element> inflatedElements = new(StringComparer.OrdinalIgnoreCase);

    internal static Task<ToolResult> RunOnMainThreadAsync(Func<ToolResult> action)
    {
        try
        {
            if (MainThread.IsMainThread)
            {
                return Task.FromResult(ExecuteSafely(action));
            }

            return MainThread.InvokeOnMainThreadAsync(() => ExecuteSafely(action));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "maui_execution_failed"));
        }
    }

    internal static ToolResult ExecuteSafely(Func<ToolResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            return ToolResult.Failure(exception.Message, errorCode: "maui_execution_failed");
        }
    }

    internal static string CurrentPlatform
    {
        get
        {
#if ANDROID
            return "android";
#elif IOS
            return "ios";
#elif MACCATALYST
            return "maccatalyst";
#else
            return "unknown";
#endif
        }
    }

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

        var modalPage = page.Navigation?.ModalStack.LastOrDefault();
        if (modalPage != null && !ReferenceEquals(modalPage, page))
        {
            return ResolveDisplayedPage(modalPage, visited);
        }

        return page switch
        {
            Shell shell when shell.CurrentPage != null => ResolveDisplayedPage(shell.CurrentPage, visited),
            NavigationPage navigationPage when navigationPage.CurrentPage != null => ResolveDisplayedPage(navigationPage.CurrentPage, visited),
            TabbedPage tabbedPage when tabbedPage.CurrentPage != null => ResolveDisplayedPage(tabbedPage.CurrentPage, visited),
            FlyoutPage flyoutPage when flyoutPage.Detail != null => ResolveDisplayedPage(flyoutPage.Detail, visited),
            _ => page
        };
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

    internal static MauiElementResolution? ResolveElementOrInflated(string nodeId)
    {
        var liveResolution = ResolveElement(nodeId);
        if (liveResolution != null)
        {
            return liveResolution;
        }

        return TryResolveInflatedElement(nodeId, out var inflatedElement) && inflatedElement != null
            ? new MauiElementResolution(inflatedElement, inflatedElement, Array.Empty<Element>())
            : null;
    }

    internal static void RegisterInflatedElement(Element element)
    {
        inflatedElements[GetElementId(element)] = element;
    }

    internal static bool ForgetInflatedElement(Element element)
        => inflatedElements.Remove(GetElementId(element));

    internal static bool TryResolveInflatedElement(string nodeId, out Element? element)
    {
        if (inflatedElements.TryGetValue(nodeId, out element))
        {
            return true;
        }

        element = inflatedElements.Values.FirstOrDefault(candidate => IsElementMatch(candidate, nodeId));
        return element != null;
    }

    internal static IReadOnlyList<MauiElementTraversalEntry> TraverseElements(Element root, int maxDepth)
    {
        var entries = new List<MauiElementTraversalEntry>();
        TraverseElements(root, maxDepth, depth: 0, new List<Element>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase), entries);
        return entries;
    }

    internal static void TraverseElements(
        Element element,
        int maxDepth,
        int depth,
        List<Element> ancestors,
        HashSet<string> visited,
        List<MauiElementTraversalEntry> entries)
    {
        if (!visited.Add(GetElementId(element)))
        {
            return;
        }

        entries.Add(new MauiElementTraversalEntry(element, ancestors.ToArray(), depth));

        if (depth >= maxDepth)
        {
            visited.Remove(GetElementId(element));
            return;
        }

        foreach (var child in GetChildElements(element))
        {
            ancestors.Add(element);
            TraverseElements(child, maxDepth, depth + 1, ancestors, visited, entries);
            ancestors.RemoveAt(ancestors.Count - 1);
        }

        visited.Remove(GetElementId(element));
    }

    internal static bool TryFindElement(
        Element element,
        string nodeId,
        List<Element> ancestors,
        HashSet<string> visited,
        out Element? result)
    {
        result = null;

        if (!visited.Add(GetElementId(element)))
        {
            return false;
        }

        if (IsElementMatch(element, nodeId))
        {
            result = element;
            return true;
        }

        foreach (var child in GetChildElements(element))
        {
            ancestors.Add(element);
            if (TryFindElement(child, nodeId, ancestors, visited, out result))
            {
                return true;
            }

            ancestors.RemoveAt(ancestors.Count - 1);
        }

        return false;
    }

    internal static bool IsElementMatch(Element element, string nodeId)
        => string.Equals(GetElementId(element), nodeId, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(element.AutomationId, nodeId, StringComparison.Ordinal);

    internal static JsonObject BuildElementNode(
        Element element,
        MauiTreeBuildOptions options,
        int depthRemaining,
        HashSet<string> visited)
    {
        var children = GetChildElements(element);
        var json = CreateElementReference(element);
        json["childCount"] = children.Count;

        if (element is VisualElement visualElement)
        {
            json["visible"] = visualElement.IsVisible;
            json["enabled"] = visualElement.IsEnabled;

            if (options.IncludeBounds)
            {
                json["bounds"] = new JsonObject
                {
                    ["x"] = visualElement.X,
                    ["y"] = visualElement.Y,
                    ["width"] = visualElement.Width,
                    ["height"] = visualElement.Height
                };
            }
        }

        if (options.IncludeBindingContexts)
        {
            json["bindingContextType"] = element.BindingContext == null
                ? null
                : CreateTypeMetadata(element.BindingContext.GetType());
        }

        if (options.IncludeProperties)
        {
            json["properties"] = CreateElementProperties(element);
        }

        if (options.IncludeBindableProperties && element is BindableObject bindable)
        {
            json["bindableProperties"] = CreateBindablePropertiesArray(bindable);
        }

        if (!visited.Add(GetElementId(element)))
        {
            json["cycle"] = true;
            return json;
        }

        if (depthRemaining > 0)
        {
            var childNodes = new JsonArray();
            foreach (var child in children)
            {
                childNodes.Add(BuildElementNode(child, options, depthRemaining - 1, visited));
            }

            json["children"] = childNodes;
        }

        visited.Remove(GetElementId(element));
        return json;
    }

    internal static IReadOnlyList<Element> GetChildElements(Element element)
    {
        var children = new List<Element>();

        if (element is IVisualTreeElement visualTreeElement)
        {
            foreach (var visualChild in visualTreeElement.GetVisualChildren())
            {
                if (visualChild is Element child)
                {
                    AddDistinctElement(children, child);
                }
            }
        }

        foreach (var propertyName in new[] { "Page", "CurrentPage", "Content", "Detail", "Flyout", "Children" })
        {
            var property = element.GetType().GetRuntimeProperty(propertyName);
            if (property == null || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(element);
            }
            catch
            {
                continue;
            }

            AddElementValue(children, value);
        }

        return children.Where(child => !ReferenceEquals(child, element)).ToArray();
    }

    internal static void AddElementValue(List<Element> children, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case Element element:
                AddDistinctElement(children, element);
                return;
            case IVisualTreeElement visualTreeElement when visualTreeElement is Element element:
                AddDistinctElement(children, element);
                return;
            case IEnumerable enumerable when value is not string:
                foreach (var item in enumerable)
                {
                    AddElementValue(children, item);
                }

                return;
        }
    }

    internal static void AddDistinctElement(List<Element> children, Element element)
    {
        var elementId = GetElementId(element);
        if (children.Any(child => string.Equals(GetElementId(child), elementId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        children.Add(element);
    }

    internal static bool TryCreateXamlRoot(string xaml, string? rootTypeName, out BindableObject? root, out string? error)
    {
        root = null;

        if (!TryResolveXamlRootType(xaml, rootTypeName, out var rootType, out error) || rootType == null)
        {
            return false;
        }

        if (!typeof(BindableObject).IsAssignableFrom(rootType))
        {
            error = $"The XAML root type '{GetTypeDisplayName(rootType)}' is not a BindableObject.";
            return false;
        }

        if (rootType.IsAbstract)
        {
            error = $"The XAML root type '{GetTypeDisplayName(rootType)}' is abstract.";
            return false;
        }

        try
        {
            root = (BindableObject?)Activator.CreateInstance(rootType);
        }
        catch (Exception exception)
        {
            error = $"Could not create XAML root type '{GetTypeDisplayName(rootType)}': {exception.Message}";
            return false;
        }

        if (root == null)
        {
            error = $"Could not create XAML root type '{GetTypeDisplayName(rootType)}'.";
            return false;
        }

        return true;
    }

    internal static bool TryResolveXamlRootType(string xaml, string? rootTypeName, out Type? rootType, out string? error)
    {
        rootType = null;
        error = null;

        if (!string.IsNullOrWhiteSpace(rootTypeName))
        {
            rootType = ResolveTypeName(rootTypeName);
            if (rootType == null)
            {
                error = $"The root type '{rootTypeName}' could not be resolved.";
                return false;
            }

            return true;
        }

        if (!TryReadXamlRootName(xaml, out var localName, out var namespaceName, out error))
        {
            return false;
        }

        if (IsMauiXamlNamespace(namespaceName))
        {
            rootType = ResolveTypeName($"Microsoft.Maui.Controls.{localName}")
                ?? ResolveTypeName($"Microsoft.Maui.Controls.Shapes.{localName}");
            if (rootType == null)
            {
                error = $"The MAUI XAML root element '{localName}' could not be resolved. Pass rootTypeName to disambiguate it.";
                return false;
            }

            return true;
        }

        if (TryParseClrNamespace(namespaceName, out var clrNamespace, out var assemblyName))
        {
            rootType = ResolveTypeName($"{clrNamespace}.{localName}", assemblyName);
            if (rootType == null)
            {
                error = $"The XAML root type '{clrNamespace}.{localName}' could not be resolved.";
                return false;
            }

            return true;
        }

        error = $"The XAML root namespace '{namespaceName}' is not supported. Pass rootTypeName to specify the root CLR type.";
        return false;
    }

    internal static bool TryReadXamlRootName(string xaml, out string localName, out string namespaceName, out string? error)
    {
        localName = string.Empty;
        namespaceName = string.Empty;
        error = null;

        try
        {
            using var stringReader = new StringReader(xaml);
            using var reader = XmlReader.Create(
                stringReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                localName = reader.LocalName;
                namespaceName = reader.NamespaceURI;
                return true;
            }
        }
        catch (Exception exception)
        {
            error = $"The XAML root element could not be read: {exception.Message}";
            return false;
        }

        error = "The XAML document does not contain a root element.";
        return false;
    }

    internal static bool IsMauiXamlNamespace(string namespaceName)
        => string.Equals(namespaceName, "http://schemas.microsoft.com/dotnet/2021/maui", StringComparison.Ordinal) ||
           string.Equals(namespaceName, "http://xamarin.com/schemas/2014/forms", StringComparison.Ordinal);

    internal static bool TryParseClrNamespace(string namespaceName, out string clrNamespace, out string? assemblyName)
    {
        clrNamespace = string.Empty;
        assemblyName = null;

        if (!namespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = namespaceName.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        clrNamespace = parts[0]["clr-namespace:".Length..];
        foreach (var part in parts.Skip(1))
        {
            if (part.StartsWith("assembly=", StringComparison.Ordinal))
            {
                assemblyName = part["assembly=".Length..];
            }
        }

        return !string.IsNullOrWhiteSpace(clrNamespace);
    }

    internal static Type? ResolveTypeName(string typeName, string? assemblyName = null)
    {
        var normalizedTypeName = typeName.Trim();
        var directType = Type.GetType(normalizedTypeName, throwOnError: false, ignoreCase: true);
        if (directType != null)
        {
            return directType;
        }

        var candidates = new List<string> { normalizedTypeName };
        if (!normalizedTypeName.Contains('.', StringComparison.Ordinal))
        {
            candidates.Add($"Microsoft.Maui.Controls.{normalizedTypeName}");
            candidates.Add($"Microsoft.Maui.Controls.Shapes.{normalizedTypeName}");
        }

        foreach (var assembly in GetCandidateAssemblies(assemblyName))
        {
            foreach (var candidate in candidates)
            {
                var candidateType = assembly.GetType(candidate, throwOnError: false, ignoreCase: true);
                if (candidateType != null)
                {
                    return candidateType;
                }
            }
        }

        if (normalizedTypeName.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var assembly in GetCandidateAssemblies(assemblyName))
        {
            var candidateType = GetLoadableTypes(assembly)
                .FirstOrDefault(type => string.Equals(type.Name, normalizedTypeName, StringComparison.OrdinalIgnoreCase));
            if (candidateType != null)
            {
                return candidateType;
            }
        }

        return null;
    }

    internal static IEnumerable<Assembly> GetCandidateAssemblies(string? assemblyName)
    {
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            if (loadedAssembly != null)
            {
                yield return loadedAssembly;
                yield break;
            }

            Assembly? resolvedAssembly = null;
            try
            {
                resolvedAssembly = Assembly.Load(new AssemblyName(assemblyName));
            }
            catch
            {
            }

            if (resolvedAssembly != null)
            {
                yield return resolvedAssembly;
            }

            yield break;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            yield return assembly;
        }
    }

    internal static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    internal static bool TryAttachElement(
        Element parent,
        Element child,
        int? index,
        bool replaceContent,
        out string? container,
        out string? error)
    {
        container = null;
        error = null;

        if (parent is Layout layout)
        {
            if (child is not IView childView)
            {
                error = $"The child '{GetTypeDisplayName(child.GetType())}' cannot be added to a Layout because it is not an IView.";
                return false;
            }

            if (index.HasValue)
            {
                if (index.Value < 0 || index.Value > layout.Children.Count)
                {
                    error = $"The index {index.Value} is outside the valid range 0..{layout.Children.Count}.";
                    return false;
                }

                layout.Children.Insert(index.Value, childView);
            }
            else
            {
                layout.Children.Add(childView);
            }

            container = "Children";
            return true;
        }

        if (index.HasValue)
        {
            error = "The index argument is only supported when adding to a Layout Children collection.";
            return false;
        }

        var contentProperty = ResolvePublicInstanceProperty(parent.GetType(), "Content");
        if (contentProperty == null || !HasPublicSetter(contentProperty))
        {
            error = $"The parent '{GetTypeDisplayName(parent.GetType())}' does not expose a supported Children collection or writable Content property.";
            return false;
        }

        if (!contentProperty.PropertyType.IsAssignableFrom(child.GetType()))
        {
            error = $"The parent Content property expects '{GetTypeDisplayName(contentProperty.PropertyType)}', but the child is '{GetTypeDisplayName(child.GetType())}'.";
            return false;
        }

        var existingContent = contentProperty.GetValue(parent);
        if (existingContent != null && !ReferenceEquals(existingContent, child) && !replaceContent)
        {
            error = $"The parent '{GetTypeDisplayName(parent.GetType())}' already has Content. Pass replaceContent=true to replace it.";
            return false;
        }

        contentProperty.SetValue(parent, child);
        container = contentProperty.Name;
        return true;
    }

    internal static bool TryDetachElement(Element child, out Element? parent, out string? container, out string? error)
    {
        parent = child.Parent;
        container = null;
        error = null;

        if (parent == null)
        {
            return true;
        }

        if (parent is Layout layout && child is IView childView)
        {
            if (layout.Children.Remove(childView))
            {
                container = "Children";
                return true;
            }
        }

        var contentProperty = ResolvePublicInstanceProperty(parent.GetType(), "Content");
        if (contentProperty != null && HasPublicSetter(contentProperty))
        {
            var existingContent = contentProperty.GetValue(parent);
            if (ReferenceEquals(existingContent, child))
            {
                contentProperty.SetValue(parent, null);
                container = contentProperty.Name;
                return true;
            }
        }

        error = $"The child '{GetTypeDisplayName(child.GetType())}' is parented by '{GetTypeDisplayName(parent.GetType())}', but no supported detach path was found.";
        return false;
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
        if (currentPage.Navigation?.ModalStack != null)
        {
            foreach (var page in currentPage.Navigation.ModalStack)
            {
                modalStack.Add(CreateElementReference(page));
            }
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

    internal static JsonObject CreateElementReference(Element element)
    {
        var json = new JsonObject
        {
            ["id"] = GetElementId(element),
            ["type"] = GetTypeDisplayName(element.GetType()),
            ["kind"] = GetElementKind(element),
            ["automationId"] = NullIfWhiteSpace(element.AutomationId),
            ["styleId"] = NullIfWhiteSpace(element.StyleId),
            ["classId"] = NullIfWhiteSpace(element.ClassId),
            ["label"] = GetElementLabel(element)
        };

        if (element is Page page)
        {
            json["title"] = CreateSafeLabel(page.Title);
        }

        return json;
    }

    internal static JsonArray CreateElementReferenceArray(IEnumerable<Element> elements)
    {
        var array = new JsonArray();
        foreach (var element in elements)
        {
            array.Add(CreateElementReference(element));
        }

        return array;
    }

    internal static JsonArray CreateElementPath(IReadOnlyList<Element> ancestors, Element element)
    {
        var path = new JsonArray();
        foreach (var ancestor in ancestors)
        {
            path.Add(CreateElementReference(ancestor));
        }

        path.Add(CreateElementReference(element));
        return path;
    }

    internal static JsonObject CreateElementMatch(MauiElementTraversalEntry entry, bool includeBounds, bool includeProperties)
    {
        var element = entry.Element;
        var json = CreateElementReference(element);
        json["depth"] = entry.Depth;
        json["path"] = CreateElementPath(entry.Ancestors, element);

        if (element is VisualElement visualElement)
        {
            json["visible"] = visualElement.IsVisible;
            json["enabled"] = visualElement.IsEnabled;

            if (includeBounds)
            {
                json["bounds"] = CreateBoundsSnapshot(visualElement);
            }
        }

        if (includeProperties)
        {
            json["properties"] = CreateElementProperties(element);
        }

        return json;
    }

    internal static JsonObject CreateBoundsSnapshot(VisualElement visualElement)
    {
        return new JsonObject
        {
            ["x"] = visualElement.X,
            ["y"] = visualElement.Y,
            ["width"] = visualElement.Width,
            ["height"] = visualElement.Height
        };
    }

    internal static JsonObject CreateElementProperties(Element element)
    {
        var properties = new JsonObject
        {
            ["parentId"] = element.Parent == null ? null : GetElementId(element.Parent),
            ["bindingContextType"] = element.BindingContext == null ? null : CreateTypeMetadata(element.BindingContext.GetType())
        };

        if (element is VisualElement visualElement)
        {
            properties["opacity"] = visualElement.Opacity;
            properties["inputTransparent"] = visualElement.InputTransparent;
            properties["zIndex"] = visualElement.ZIndex;
            properties["anchorX"] = visualElement.AnchorX;
            properties["anchorY"] = visualElement.AnchorY;
            properties["scale"] = visualElement.Scale;
            properties["scaleX"] = visualElement.ScaleX;
            properties["scaleY"] = visualElement.ScaleY;
            properties["rotation"] = visualElement.Rotation;
            properties["rotationX"] = visualElement.RotationX;
            properties["rotationY"] = visualElement.RotationY;
            properties["translationX"] = visualElement.TranslationX;
            properties["translationY"] = visualElement.TranslationY;
            properties["minimumWidthRequest"] = visualElement.MinimumWidthRequest;
            properties["minimumHeightRequest"] = visualElement.MinimumHeightRequest;
            properties["widthRequest"] = visualElement.WidthRequest;
            properties["heightRequest"] = visualElement.HeightRequest;
        }

        if (element is View view)
        {
            properties["margin"] = view.Margin.ToString();
            properties["horizontalOptions"] = view.HorizontalOptions.ToString();
            properties["verticalOptions"] = view.VerticalOptions.ToString();
        }

        if (element is Page page)
        {
            properties["padding"] = page.Padding.ToString();
            properties["backgroundColor"] = page.BackgroundColor?.ToArgbHex();
        }

        if (element is IView mauiView)
        {
            properties["flowDirection"] = mauiView.FlowDirection.ToString();
        }

        return properties;
    }

    internal static string GetElementKind(Element element)
    {
        return element switch
        {
            Window => "window",
            Shell => "shell",
            Page => "page",
            Layout => "layout",
            View => "view",
            MenuItem => "menuItem",
            _ => "element"
        };
    }

    internal static string? GetElementLabel(Element element)
    {
        string? label = element switch
        {
            Entry entry => CreateInputPlaceholderLabel(entry.Placeholder, entry.IsPassword),
            Editor editor => CreateSafeLabel(editor.Placeholder),
            SearchBar searchBar => CreateSafeLabel(searchBar.Placeholder),
            Picker picker => CreateSafeLabel(picker.Title),
            DatePicker or TimePicker or CheckBox or Slider or Stepper => null,
            Label labelElement => CreateSafeLabel(labelElement.Text),
            Button button => CreateSafeLabel(button.Text),
            Page page => CreateSafeLabel(page.Title),
            MenuItem menuItem => CreateSafeLabel(menuItem.Text),
            _ => null
        };

        return label;
    }

    internal static string GetElementId(Element element) => element.Id.ToString("N");

    internal static JsonArray CreateBindablePropertiesArray(BindableObject bindable)
    {
        var properties = new JsonArray();
        foreach (var descriptor in GetBindablePropertyDescriptors(bindable.GetType()))
        {
            properties.Add(CreateBindablePropertyMetadata(bindable, descriptor));
        }

        return properties;
    }

    internal static BindablePropertyDescriptor? ResolveBindableProperty(
        BindableObject bindable,
        string propertyName,
        string? declaringTypeName)
    {
        var normalizedPropertyName = propertyName.Trim();
        var descriptors = GetBindablePropertyDescriptors(bindable.GetType());
        var matches = descriptors
            .Where(descriptor => IsBindablePropertyMatch(descriptor, normalizedPropertyName))
            .Where(descriptor => string.IsNullOrWhiteSpace(declaringTypeName) || IsTypeNameMatch(descriptor.DeclaringType, declaringTypeName!))
            .ToArray();

        if (matches.Length == 1)
        {
            return matches[0];
        }

        return matches.FirstOrDefault(descriptor => string.Equals(descriptor.BindableProperty.PropertyName, normalizedPropertyName, StringComparison.Ordinal))
            ?? matches.FirstOrDefault();
    }

    internal static bool IsBindablePropertyMatch(BindablePropertyDescriptor descriptor, string propertyName)
    {
        var memberNameWithoutSuffix = descriptor.MemberName.EndsWith("Property", StringComparison.Ordinal)
            ? descriptor.MemberName[..^"Property".Length]
            : descriptor.MemberName;

        return string.Equals(descriptor.BindableProperty.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(descriptor.MemberName, propertyName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(memberNameWithoutSuffix, propertyName, StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<BindablePropertyDescriptor> GetBindablePropertyDescriptors(Type type)
    {
        var descriptors = new List<BindablePropertyDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var currentType = type; currentType != null; currentType = currentType.BaseType)
        {
            foreach (var field in currentType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!typeof(BindableProperty).IsAssignableFrom(field.FieldType) ||
                    field.GetValue(null) is not BindableProperty bindableProperty)
                {
                    continue;
                }

                AddDescriptor(descriptors, seen, bindableProperty, field.Name, currentType);
            }

            foreach (var property in currentType.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!typeof(BindableProperty).IsAssignableFrom(property.PropertyType) ||
                    property.GetIndexParameters().Length > 0 ||
                    property.GetValue(null) is not BindableProperty bindableProperty)
                {
                    continue;
                }

                AddDescriptor(descriptors, seen, bindableProperty, property.Name, currentType);
            }
        }

        return descriptors;
    }

    internal static void AddDescriptor(
        List<BindablePropertyDescriptor> descriptors,
        HashSet<string> seen,
        BindableProperty bindableProperty,
        string memberName,
        Type declaringType)
    {
        var key = $"{declaringType.FullName}|{memberName}";
        if (!seen.Add(key))
        {
            return;
        }

        descriptors.Add(new BindablePropertyDescriptor(bindableProperty, memberName, declaringType));
    }

    internal static JsonObject CreateBindablePropertyMetadata(BindableObject bindable, BindablePropertyDescriptor descriptor)
    {
        return new JsonObject
        {
            ["name"] = descriptor.BindableProperty.PropertyName,
            ["memberName"] = descriptor.MemberName,
            ["declaringType"] = CreateTypeMetadata(descriptor.DeclaringType),
            ["valueType"] = CreateTypeMetadata(descriptor.BindableProperty.ReturnType),
            ["defaultBindingMode"] = descriptor.BindableProperty.DefaultBindingMode.ToString(),
            ["isSet"] = IsBindablePropertySet(bindable, descriptor.BindableProperty),
            ["hasBinding"] = GetBinding(bindable, descriptor.BindableProperty) != null
        };
    }

    internal static bool IsBindablePropertySet(BindableObject bindable, BindableProperty bindableProperty)
    {
        try
        {
            return bindable.IsSet(bindableProperty);
        }
        catch
        {
            return false;
        }
    }

    internal static JsonObject? CreateBindingInfo(BindableObject bindable, BindableProperty bindableProperty)
    {
        var binding = GetBinding(bindable, bindableProperty);
        if (binding == null)
        {
            return null;
        }

        var json = new JsonObject
        {
            ["type"] = GetTypeDisplayName(binding.GetType())
        };

        foreach (var propertyName in new[] { "Mode", "Path", "StringFormat", "FallbackValue", "TargetNullValue", "Source" })
        {
            if (!TryReadPublicProperty(binding, propertyName, out var value, out var propertyType))
            {
                continue;
            }

            json[ToCamelCase(propertyName)] = CreateValueSnapshot(value, propertyType, depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
        }

        return json;
    }

    internal static object? GetBinding(BindableObject bindable, BindableProperty bindableProperty)
    {
        try
        {
            var method = typeof(BindableObject).GetMethod(
                "GetBinding",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(BindableProperty) },
                modifiers: null);

            return method?.Invoke(bindable, new object[] { bindableProperty });
        }
        catch
        {
            return null;
        }
    }

    internal static bool RemoveBinding(BindableObject bindable, BindableProperty bindableProperty)
    {
        try
        {
            var method = typeof(BindableObject).GetMethod(
                "RemoveBinding",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(BindableProperty) },
                modifiers: null);

            method?.Invoke(bindable, new object[] { bindableProperty });
            return method != null;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryReadPublicProperty(object target, string propertyName, out object? value, out Type? propertyType)
    {
        value = null;
        propertyType = null;

        var property = ResolvePublicInstanceProperty(target.GetType(), propertyName);
        if (property == null || property.GetIndexParameters().Length > 0)
        {
            return false;
        }

        try
        {
            value = property.GetValue(target);
            propertyType = property.PropertyType;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TrySetPublicPropertyFromJson(object target, string propertyName, string valueJson, out object? updatedValue, out string? error)
    {
        updatedValue = null;
        error = null;

        var property = ResolvePublicInstanceProperty(target.GetType(), propertyName);
        if (property == null)
        {
            error = $"The property '{propertyName}' was not found on '{GetTypeDisplayName(target.GetType())}'.";
            return false;
        }

        if (!HasPublicSetter(property))
        {
            error = $"The property '{property.Name}' is not publicly writable.";
            return false;
        }

        object? convertedValue;
        try
        {
            convertedValue = ConvertJsonArgument(valueJson, property.PropertyType);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        try
        {
            property.SetValue(target, convertedValue);
            updatedValue = property.GetValue(target);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal static bool TryResolveCommand(
        object target,
        string commandName,
        out ICommand? command,
        out object? commandParameter,
        out string? matchedPropertyName)
    {
        command = null;
        commandParameter = null;
        matchedPropertyName = null;

        var candidates = new[]
        {
            commandName,
            commandName.EndsWith("Command", StringComparison.OrdinalIgnoreCase) ? commandName : $"{commandName}Command"
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var property = ResolvePublicInstanceProperty(target.GetType(), candidate);
            if (property == null || !typeof(ICommand).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            command = property.GetValue(target) as ICommand;
            matchedPropertyName = property.Name;

            var parameterPropertyName = property.Name.EndsWith("Command", StringComparison.Ordinal)
                ? $"{property.Name[..^"Command".Length]}CommandParameter"
                : $"{property.Name}Parameter";
            if (TryReadPublicProperty(target, parameterPropertyName, out var parameterValue, out _))
            {
                commandParameter = parameterValue;
            }

            return command != null;
        }

        return false;
    }

    internal static bool TryExecuteCommand(
        ICommand command,
        object? parameter,
        bool requireCanExecute,
        out string? error)
    {
        error = null;

        bool canExecute;
        try
        {
            canExecute = command.CanExecute(parameter);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        if (!canExecute && requireCanExecute)
        {
            error = "The command returned false from CanExecute.";
            return false;
        }

        try
        {
            command.Execute(parameter);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal static JsonObject CreateValueSnapshot(
        object? value,
        Type? declaredType,
        int depthRemaining,
        int maxItems,
        int maxProperties)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return CreateValueSnapshotCore(value, declaredType, depthRemaining, maxItems, maxProperties, visited);
    }

    internal static JsonObject CreateValueMetadataSnapshot(object? value, Type? declaredType)
    {
        var runtimeType = value?.GetType();
        var effectiveType = runtimeType ?? declaredType;
        var json = new JsonObject
        {
            ["isNull"] = value == null,
            ["kind"] = GetValueKind(value, effectiveType),
            ["declaredType"] = declaredType == null ? null : CreateTypeMetadata(declaredType),
            ["runtimeType"] = runtimeType == null ? null : CreateTypeMetadata(runtimeType)
        };

        if (value is Element element)
        {
            json["element"] = CreateElementReference(element);
        }

        return json;
    }

    internal static JsonObject CreateResourceDictionarySnapshot(
        object resources,
        string scope,
        object owner,
        bool includeValues,
        bool includeMergedDictionaries,
        int maxEntries)
    {
        var json = new JsonObject
        {
            ["scope"] = scope,
            ["ownerType"] = CreateTypeMetadata(owner.GetType()),
            ["resourceType"] = CreateTypeMetadata(resources.GetType())
        };

        if (owner is Element ownerElement)
        {
            json["owner"] = CreateElementReference(ownerElement);
        }

        var entries = new JsonArray();
        var count = 0;
        foreach (var entry in EnumerateDictionaryEntries(resources))
        {
            if (count >= maxEntries)
            {
                json["entriesTruncated"] = true;
                break;
            }

            var entryJson = new JsonObject
            {
                ["key"] = entry.Key,
                ["valueType"] = entry.Value == null ? null : CreateTypeMetadata(entry.Value.GetType())
            };

            if (includeValues)
            {
                entryJson["stringValue"] = CreateSafeLabel(entry.Value?.ToString());
                entryJson["value"] = CreateValueSnapshot(entry.Value, entry.Value?.GetType(), depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
            }

            entries.Add(entryJson);
            count++;
        }

        json["entries"] = entries;
        json["entryCount"] = count;

        if (includeMergedDictionaries && TryReadPublicProperty(resources, "MergedDictionaries", out var mergedDictionaries, out _) &&
            mergedDictionaries is IEnumerable enumerable)
        {
            var merged = new JsonArray();
            foreach (var dictionary in enumerable)
            {
                if (dictionary == null)
                {
                    continue;
                }

                merged.Add(new JsonObject
                {
                    ["type"] = CreateTypeMetadata(dictionary.GetType()),
                    ["entryCount"] = EnumerateDictionaryEntries(dictionary).Count
                });
            }

            json["mergedDictionaries"] = merged;
        }

        return json;
    }

    internal static IReadOnlyList<ResourceEntry> EnumerateDictionaryEntries(object dictionary)
    {
        var entries = new List<ResourceEntry>();

        if (dictionary is IDictionary nonGenericDictionary)
        {
            foreach (DictionaryEntry entry in nonGenericDictionary)
            {
                entries.Add(new ResourceEntry(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty, entry.Value));
            }

            return entries;
        }

        if (dictionary is not IEnumerable enumerable)
        {
            return entries;
        }

        foreach (var item in enumerable)
        {
            if (item == null)
            {
                continue;
            }

            var itemType = item.GetType();
            var keyProperty = itemType.GetRuntimeProperty("Key");
            var valueProperty = itemType.GetRuntimeProperty("Value");
            if (keyProperty == null || valueProperty == null)
            {
                continue;
            }

            object? key;
            object? value;
            try
            {
                key = keyProperty.GetValue(item);
                value = valueProperty.GetValue(item);
            }
            catch
            {
                continue;
            }

            entries.Add(new ResourceEntry(Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty, value));
        }

        return entries;
    }

    internal static JsonObject CreateValueSnapshotCore(
        object? value,
        Type? declaredType,
        int depthRemaining,
        int maxItems,
        int maxProperties,
        HashSet<object> visited)
    {
        var runtimeType = value?.GetType();
        var effectiveType = runtimeType ?? declaredType;
        var json = new JsonObject
        {
            ["isNull"] = value == null,
            ["kind"] = GetValueKind(value, effectiveType),
            ["declaredType"] = declaredType == null ? null : CreateTypeMetadata(declaredType),
            ["runtimeType"] = runtimeType == null ? null : CreateTypeMetadata(runtimeType)
        };

        if (value == null)
        {
            return json;
        }

        if (value is Element element)
        {
            json["element"] = CreateElementReference(element);
            return json;
        }

        var simpleValue = CreateSimpleJsonValue(value);
        if (simpleValue != null)
        {
            json["value"] = simpleValue;
            json["stringValue"] = Truncate(Convert.ToString(value, CultureInfo.InvariantCulture));
            return json;
        }

        if (depthRemaining <= 0)
        {
            return json;
        }

        if (!effectiveType!.IsValueType && !visited.Add(value))
        {
            json["cycle"] = true;
            return json;
        }

        if (value is IEnumerable enumerable and not string)
        {
            var items = new JsonArray();
            var count = 0;
            var truncated = false;
            foreach (var item in enumerable)
            {
                if (count >= maxItems)
                {
                    truncated = true;
                    break;
                }

                items.Add(CreateValueSnapshotCore(item, item?.GetType(), depthRemaining - 1, maxItems, maxProperties, visited));
                count++;
            }

            json["items"] = items;
            json["truncated"] = truncated;
            return json;
        }

        var properties = new JsonObject();
        var propertyCount = 0;
        foreach (var property in effectiveType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (propertyCount >= maxProperties)
            {
                json["propertiesTruncated"] = true;
                break;
            }

            if (property.GetIndexParameters().Length > 0 || !property.CanRead)
            {
                continue;
            }

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            properties[property.Name] = CreateValueSnapshotCore(propertyValue, property.PropertyType, depthRemaining - 1, maxItems, maxProperties, visited);
            propertyCount++;
        }

        json["properties"] = properties;
        return json;
    }

    internal static string GetValueKind(object? value, Type? type)
    {
        if (value == null)
        {
            return "null";
        }

        type ??= value.GetType();
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (value is Element)
        {
            return "maui_element";
        }

        if (underlyingType == typeof(string) || underlyingType == typeof(char) || underlyingType == typeof(Guid))
        {
            return "string";
        }

        if (underlyingType == typeof(bool))
        {
            return "boolean";
        }

        if (underlyingType.IsEnum)
        {
            return "enum";
        }

        if (IsNumericType(underlyingType))
        {
            return "number";
        }

        if (underlyingType == typeof(DateTime) ||
            underlyingType == typeof(DateTimeOffset) ||
            underlyingType == typeof(TimeSpan))
        {
            return "temporal";
        }

        if (value is IEnumerable and not string)
        {
            return "collection";
        }

        return "object";
    }

    internal static JsonNode? CreateSimpleJsonValue(object value)
    {
        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (type.IsEnum)
        {
            return JsonValue.Create(value.ToString());
        }

        if (type == typeof(string))
        {
            return JsonValue.Create((string)value);
        }

        if (type == typeof(char) ||
            type == typeof(Guid) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan))
        {
            return JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        if (type == typeof(bool))
        {
            return JsonValue.Create((bool)value);
        }

        if (type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong))
        {
            return JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return JsonValue.Create(Convert.ToDouble(value, CultureInfo.InvariantCulture));
        }

        return null;
    }

    internal static object? ConvertJsonArgument(string rawValue, Type targetType)
    {
        var node = ParseJsonArgument(rawValue);
        return ConvertJsonValue(node, targetType);
    }

    internal static object? ConvertJsonArgumentToUntyped(string rawValue)
    {
        var node = ParseJsonArgument(rawValue);
        return ConvertJsonNodeToUntyped(node);
    }

    internal static object? ConvertJsonNodeToUntyped(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonValue jsonValue => ConvertJsonValueToUntyped(jsonValue),
            JsonArray jsonArray => jsonArray.Select(ConvertJsonNodeToUntyped).ToArray(),
            JsonObject jsonObject => jsonObject.ToDictionary(property => property.Key, property => ConvertJsonNodeToUntyped(property.Value), StringComparer.Ordinal),
            _ => node.ToJsonString()
        };
    }

    internal static object? ConvertJsonValueToUntyped(JsonValue jsonValue)
    {
        if (jsonValue.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (jsonValue.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return jsonValue.ToString();
    }

    internal static bool AreValuesEquivalent(object? actualValue, Type targetType, string expectedJson)
    {
        object? expectedValue;
        try
        {
            expectedValue = ConvertJsonArgument(expectedJson, targetType);
        }
        catch
        {
            expectedValue = ConvertJsonArgumentToUntyped(expectedJson);
        }

        if (actualValue == null || expectedValue == null)
        {
            return actualValue == null && expectedValue == null;
        }

        if (Equals(actualValue, expectedValue))
        {
            return true;
        }

        return string.Equals(
            Convert.ToString(actualValue, CultureInfo.InvariantCulture),
            Convert.ToString(expectedValue, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    internal static JsonNode? ParseJsonArgument(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return JsonValue.Create(string.Empty);
        }

        try
        {
            return JsonNode.Parse(rawValue);
        }
        catch (JsonException)
        {
            return JsonValue.Create(rawValue);
        }
    }

    internal static object? ConvertJsonValue(JsonNode? node, Type targetType)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        var effectiveType = nullableType ?? targetType;

        if (node == null)
        {
            if (nullableType != null || !targetType.IsValueType)
            {
                return null;
            }

            throw new InvalidOperationException($"The target type '{GetTypeDisplayName(targetType)}' does not accept null.");
        }

        if (effectiveType == typeof(string))
        {
            return GetScalarString(node);
        }

        if (effectiveType == typeof(bool))
        {
            return GetBooleanValue(node);
        }

        if (effectiveType == typeof(char))
        {
            var text = GetScalarString(node);
            return text.Length == 1 ? text[0] : throw new InvalidOperationException("Character values must contain exactly one character.");
        }

        if (effectiveType.IsEnum)
        {
            return ConvertEnumValue(node, effectiveType);
        }

        if (IsNumericType(effectiveType))
        {
            return Convert.ChangeType(GetScalarValue(node), effectiveType, CultureInfo.InvariantCulture);
        }

        if (effectiveType == typeof(Guid))
        {
            return Guid.Parse(GetScalarString(node));
        }

        if (effectiveType == typeof(DateTime))
        {
            return DateTime.Parse(GetScalarString(node), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (effectiveType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(GetScalarString(node), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (effectiveType == typeof(TimeSpan))
        {
            return TimeSpan.Parse(GetScalarString(node), CultureInfo.InvariantCulture);
        }

        if (effectiveType == typeof(Color))
        {
            return ConvertColorValue(node);
        }

        if (effectiveType == typeof(Thickness))
        {
            return ConvertThicknessValue(node);
        }

        if (effectiveType == typeof(GridLength))
        {
            return ConvertGridLengthValue(node);
        }

        if (effectiveType == typeof(Point))
        {
            return ConvertPointValue(node);
        }

        if (effectiveType == typeof(Size))
        {
            return ConvertSizeValue(node);
        }

        if (effectiveType == typeof(Rect))
        {
            return ConvertRectValue(node);
        }

        try
        {
            return JsonSerializer.Deserialize(node.ToJsonString(), effectiveType, jsonSerializerOptions);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Could not convert value to '{GetTypeDisplayName(targetType)}': {exception.Message}", exception);
        }
    }

    internal static object ConvertEnumValue(JsonNode node, Type enumType)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var intValue))
        {
            return Enum.ToObject(enumType, intValue);
        }

        return Enum.Parse(enumType, GetScalarString(node), ignoreCase: true);
    }

    internal static object ConvertColorValue(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            var red = GetObjectDouble(jsonObject, "red", "r");
            var green = GetObjectDouble(jsonObject, "green", "g");
            var blue = GetObjectDouble(jsonObject, "blue", "b");
            var alpha = GetObjectDouble(jsonObject, "alpha", "a", defaultValue: 1d);
            return Color.FromRgba(red, green, blue, alpha);
        }

        return Color.FromArgb(GetScalarString(node));
    }

    internal static object ConvertThicknessValue(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            return new Thickness(
                GetObjectDouble(jsonObject, "left"),
                GetObjectDouble(jsonObject, "top"),
                GetObjectDouble(jsonObject, "right"),
                GetObjectDouble(jsonObject, "bottom"));
        }

        if (TryGetDouble(node, out var uniformValue))
        {
            return new Thickness(uniformValue);
        }

        var parts = SplitNumbers(GetScalarString(node));
        return parts.Length switch
        {
            1 => new Thickness(parts[0]),
            2 => new Thickness(parts[0], parts[1]),
            4 => new Thickness(parts[0], parts[1], parts[2], parts[3]),
            _ => throw new InvalidOperationException("Thickness values must be a number, two numbers, four numbers, or an object with left/top/right/bottom.")
        };
    }

    internal static object ConvertGridLengthValue(JsonNode node)
    {
        if (TryGetDouble(node, out var absoluteValue))
        {
            return new GridLength(absoluteValue);
        }

        var text = GetScalarString(node).Trim();
        if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return GridLength.Auto;
        }

        if (text.EndsWith("*", StringComparison.Ordinal))
        {
            var multiplierText = text[..^1].Trim();
            var multiplier = string.IsNullOrWhiteSpace(multiplierText)
                ? 1d
                : double.Parse(multiplierText, CultureInfo.InvariantCulture);
            return new GridLength(multiplier, GridUnitType.Star);
        }

        return new GridLength(double.Parse(text, CultureInfo.InvariantCulture));
    }

    internal static object ConvertPointValue(JsonNode node)
    {
        if (node is not JsonObject jsonObject)
        {
            var parts = SplitNumbers(GetScalarString(node));
            return parts.Length == 2
                ? new Point(parts[0], parts[1])
                : throw new InvalidOperationException("Point values must be two numbers or an object with x/y.");
        }

        return new Point(GetObjectDouble(jsonObject, "x"), GetObjectDouble(jsonObject, "y"));
    }

    internal static object ConvertSizeValue(JsonNode node)
    {
        if (node is not JsonObject jsonObject)
        {
            var parts = SplitNumbers(GetScalarString(node));
            return parts.Length == 2
                ? new Size(parts[0], parts[1])
                : throw new InvalidOperationException("Size values must be two numbers or an object with width/height.");
        }

        return new Size(GetObjectDouble(jsonObject, "width"), GetObjectDouble(jsonObject, "height"));
    }

    internal static object ConvertRectValue(JsonNode node)
    {
        if (node is not JsonObject jsonObject)
        {
            var parts = SplitNumbers(GetScalarString(node));
            return parts.Length == 4
                ? new Rect(parts[0], parts[1], parts[2], parts[3])
                : throw new InvalidOperationException("Rect values must be four numbers or an object with x/y/width/height.");
        }

        return new Rect(
            GetObjectDouble(jsonObject, "x"),
            GetObjectDouble(jsonObject, "y"),
            GetObjectDouble(jsonObject, "width"),
            GetObjectDouble(jsonObject, "height"));
    }

    internal static object GetScalarValue(JsonNode node)
    {
        if (node is not JsonValue jsonValue)
        {
            return node.ToJsonString();
        }

        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (jsonValue.TryGetValue<decimal>(out var decimalValue))
        {
            return decimalValue;
        }

        if (jsonValue.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return jsonValue.ToString();
    }

    internal static string GetScalarString(JsonNode node)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return node.ToJsonString();
    }

    internal static bool GetBooleanValue(JsonNode node)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        return bool.Parse(GetScalarString(node));
    }

    internal static bool TryGetDouble(JsonNode node, out double value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<double>(out value))
        {
            return true;
        }

        return double.TryParse(GetScalarString(node), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    internal static double GetObjectDouble(JsonObject jsonObject, string name, string? alternateName = null, double? defaultValue = null)
    {
        var node = jsonObject[name] ?? (alternateName == null ? null : jsonObject[alternateName]);
        if (node != null && TryGetDouble(node, out var value))
        {
            return value;
        }

        if (defaultValue.HasValue)
        {
            return defaultValue.Value;
        }

        throw new InvalidOperationException($"The numeric property '{name}' is required.");
    }

    internal static double[] SplitNumbers(string value)
    {
        return value
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
    }

    internal static bool IsTypeNameMatch(Type type, string typeName)
        => string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase);

    internal sealed record MauiTreeBuildOptions(
        bool IncludeBounds,
        bool IncludeProperties,
        bool IncludeBindableProperties,
        bool IncludeBindingContexts);

    internal sealed record MauiElementResolution(
        Element Root,
        Element Element,
        IReadOnlyList<Element> Ancestors);

    internal sealed record MauiElementTraversalEntry(
        Element Element,
        IReadOnlyList<Element> Ancestors,
        int Depth);

    internal sealed record BindablePropertyDescriptor(
        BindableProperty BindableProperty,
        string MemberName,
        Type DeclaringType);

    internal sealed record ResourceEntry(
        string Key,
        object? Value);

    internal sealed record ResourceScopeTarget(
        string Scope,
        object Owner);
#else
    internal static ToolResult CreateUnsupportedResult()
        => ToolResult.Failure(".NET MAUI tools are only supported on Android, iOS, and Mac Catalyst MAUI targets.", errorCode: "maui_platform_unsupported");
#endif

    internal static int GetInt(IReadOnlyDictionary<string, string> arguments, string key, int defaultValue, int minimum, int maximum)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            throw new InvalidOperationException($"The argument '{key}' must be an integer.");
        }

        return Math.Clamp(parsedValue, minimum, maximum);
    }

    internal static int? GetOptionalInt(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            throw new InvalidOperationException($"The argument '{key}' must be an integer.");
        }

        return parsedValue;
    }

    internal static bool GetBoolean(IReadOnlyDictionary<string, string> arguments, string key, bool defaultValue)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (bool.TryParse(rawValue, out var boolValue))
        {
            return boolValue;
        }

        return rawValue switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidOperationException($"The argument '{key}' must be a boolean.")
        };
    }

    internal static string? GetString(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    internal static string GetRequiredString(IReadOnlyDictionary<string, string> arguments, string key)
        => GetString(arguments, key) ?? throw new InvalidOperationException($"The argument '{key}' is required.");

    internal static PropertyInfo? ResolvePublicInstanceProperty(Type type, string propertyName)
    {
        return type
            .GetRuntimeProperties()
            .Where(property => property.GetMethod is { IsStatic: false, IsPublic: true })
            .Where(property => property.GetIndexParameters().Length == 0)
            .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasPublicSetter(PropertyInfo property)
        => property.SetMethod is { IsStatic: false, IsPublic: true };

    internal static string? CreateSafeLabel(string? value)
    {
        var trimmedValue = NullIfWhiteSpace(value?.Trim());
        if (trimmedValue == null)
        {
            return null;
        }

        return LooksSensitiveText(trimmedValue)
            ? RedactedLabel
            : Truncate(trimmedValue);
    }

    internal static string? CreateInputPlaceholderLabel(string? placeholder, bool isSensitiveInput)
    {
        if (isSensitiveInput)
        {
            return RedactedLabel;
        }

        return CreateSafeLabel(placeholder);
    }

    internal static string? CreateSafeNavigationLocation(string? value)
    {
        var trimmedValue = NullIfWhiteSpace(value?.Trim());
        if (trimmedValue == null)
        {
            return null;
        }

        var sensitiveSuffixIndex = trimmedValue.IndexOfAny(['?', '#']);
        if (sensitiveSuffixIndex >= 0)
        {
            trimmedValue = trimmedValue[..sensitiveSuffixIndex];
        }

        return CreateSafeLabel(trimmedValue);
    }

    internal static bool LooksSensitiveText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var keyword in sensitiveLabelKeywords)
        {
            if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return LooksLikeEmailAddress(value) || HasLongDigitSequence(value);
    }

    private static bool LooksLikeEmailAddress(string value)
    {
        var atIndex = value.IndexOf('@');
        if (atIndex <= 0 || atIndex >= value.Length - 3)
        {
            return false;
        }

        var dotIndex = value.IndexOf('.', atIndex + 2);
        return dotIndex > atIndex + 1 && dotIndex < value.Length - 1;
    }

    private static bool HasLongDigitSequence(string value)
    {
        var digitCount = 0;
        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                digitCount++;
                if (digitCount >= 10)
                {
                    return true;
                }

                continue;
            }

            if (character is not (' ' or '-' or '(' or ')' or '.'))
            {
                digitCount = 0;
            }
        }

        return false;
    }

    internal static JsonObject CreateTypeMetadata(Type type)
    {
        return new JsonObject
        {
            ["name"] = type.Name,
            ["fullName"] = type.FullName ?? type.Name,
            ["namespace"] = type.Namespace,
            ["assemblyName"] = type.Assembly.GetName().Name
        };
    }

    internal static string GetTypeDisplayName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var genericTypeName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var tickIndex = genericTypeName.IndexOf('`', StringComparison.Ordinal);
        if (tickIndex >= 0)
        {
            genericTypeName = genericTypeName[..tickIndex];
        }

        return $"{genericTypeName}<{string.Join(", ", type.GetGenericArguments().Select(GetTypeDisplayName))}>";
    }

    internal static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal);
    }

    internal static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    internal static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= MaximumStringLength)
        {
            return value;
        }

        return value[..MaximumStringLength];
    }

    internal static string ToCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || char.IsLower(value[0]))
        {
            return value;
        }

        return string.Concat(char.ToLowerInvariant(value[0]), value[1..]);
    }
}
