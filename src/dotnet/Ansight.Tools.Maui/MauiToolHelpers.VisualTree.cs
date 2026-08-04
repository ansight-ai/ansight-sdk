namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
#if ANDROID
using AndroidX.RecyclerView.Widget;
using AView = Android.Views.View;
#elif IOS || MACCATALYST
using CoreGraphics;
using UIKit;
#endif

internal static partial class MauiToolHelpers
{
    private static readonly string[] ChildElementPropertyNames =
    [
        "Page",
        "CurrentPage",
        "Content",
        "Detail",
        "Flyout",
        "Children"
    ];

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ChildElementPropertiesByType = new();

    internal static JsonArray CreateBoundsColumns()
    {
        return new JsonArray(
            "x",
            "y",
            "width",
            "height",
            "absoluteX",
            "absoluteY",
            "absoluteWidth",
            "absoluteHeight");
    }

    internal static JsonObject CreateNodeFlagBits()
    {
        return new JsonObject
        {
            ["visible"] = 1,
            ["enabled"] = 2,
            ["currentPage"] = 4,
            ["activePage"] = 8,
            ["cycle"] = 16,
            ["truncated"] = 32,
            ["childrenTruncated"] = 64,
            ["custom"] = 128
        };
    }

    internal static IReadOnlyList<MauiElementTraversalEntry> TraverseElements(Element root, int maxDepth)
    {
        return TraverseElements(root, maxDepth, MauiElementTraversalOptions.Full);
    }

    internal static IReadOnlyList<MauiElementTraversalEntry> TraverseElements(
        Element root,
        int maxDepth,
        MauiElementTraversalOptions traversalOptions)
    {
        var entries = new List<MauiElementTraversalEntry>();
        TraverseElements(root, maxDepth, depth: 0, new List<Element>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase), entries, traversalOptions);
        return entries;
    }

    internal static void TraverseElements(
        Element element,
        int maxDepth,
        int depth,
        List<Element> ancestors,
        HashSet<string> visited,
        List<MauiElementTraversalEntry> entries,
        MauiElementTraversalOptions traversalOptions)
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

        foreach (var child in GetChildElements(element, traversalOptions))
        {
            ancestors.Add(element);
            TraverseElements(child, maxDepth, depth + 1, ancestors, visited, entries, traversalOptions);
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
        return TryFindElement(element, nodeId, ancestors, visited, MauiElementTraversalOptions.Full, out result);
    }

    internal static bool TryFindElement(
        Element element,
        string nodeId,
        List<Element> ancestors,
        HashSet<string> visited,
        MauiElementTraversalOptions traversalOptions,
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

        foreach (var child in GetChildElements(element, traversalOptions))
        {
            ancestors.Add(element);
            if (TryFindElement(child, nodeId, ancestors, visited, traversalOptions, out result))
            {
                return true;
            }

            ancestors.RemoveAt(ancestors.Count - 1);
        }

        visited.Remove(GetElementId(element));
        return false;
    }

    internal static bool IsElementMatch(Element element, string nodeId)
        => string.Equals(GetElementId(element), nodeId, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(element.AutomationId, nodeId, StringComparison.Ordinal);

    internal static JsonObject BuildElementNode(
        Element element,
        MauiTreeBuildOptions options,
        int depthRemaining,
        HashSet<string> visited,
        MauiTreeBuildState state,
        bool parentIsInActivePage)
    {
        var json = CreateCompactElementReference(element, options.TypeRegistry);
        json["visual"] = CreateElementVisual(element);
        if (!state.TryIncludeNode())
        {
            json["childCount"] = 0;
            json["flags"] = ReadInt(json, "flags") | 32;
            return json;
        }

        var elementId = GetElementId(element);
        var isCurrentPage = !string.IsNullOrWhiteSpace(options.CurrentPageId)
                            && string.Equals(elementId, options.CurrentPageId, StringComparison.OrdinalIgnoreCase);
        var isInActivePage = parentIsInActivePage || isCurrentPage;
        var flags = 0;

        if (element is VisualElement visualElement)
        {
            if (visualElement.IsVisible)
            {
                flags |= 1;
            }

            if (visualElement.IsEnabled)
            {
                flags |= 2;
            }

            if (options.IncludeBounds)
            {
                json["bounds"] = CreateCompactBoundsSnapshot(visualElement);
            }
        }
        else
        {
            flags |= 1 | 2;
        }

        if (isCurrentPage)
        {
            flags |= 4;
        }

        if (isInActivePage)
        {
            flags |= 8;
        }

        if (options.IncludeBindingContexts)
        {
            json["bindingContextTypeId"] = element.BindingContext == null
                ? null
                : options.TypeRegistry.GetTypeId(element.BindingContext.GetType());
        }

        if (options.IncludeProperties)
        {
            json["properties"] = CreateElementProperties(element, options.TypeRegistry);
        }

        if (options.IncludeBindableProperties && element is BindableObject bindable)
        {
            json["bindableProperties"] = CreateBindablePropertiesArray(bindable, options.TypeRegistry);
        }

        if (!visited.Add(elementId))
        {
            flags |= 16;
            json["flags"] = flags;
            json["childCount"] = 0;
            return json;
        }

        if (depthRemaining <= 0)
        {
            json["childCount"] = 0;
            json["flags"] = flags;
            visited.Remove(elementId);
            return json;
        }

        var customWalkerErrors = new List<string>();
        var customBuilderErrors = new List<string>();
        var children = GetChildElements(element, options.TraversalOptions, customWalkerErrors);
        var customChildren = GetCustomVisualTreeChildNodes(
            element,
            options,
            depthRemaining - 1,
            customBuilderErrors);
        json["childCount"] = children.Count + customChildren.Count;
        if (children.Count > 0 || customChildren.Count > 0)
        {
            var childNodes = new JsonArray();
            foreach (var child in children)
            {
                if (state.NodeCount >= options.MaxNodes)
                {
                    state.MarkTruncated();
                    flags |= 64;
                    break;
                }

                childNodes.Add(BuildElementNode(child, options, depthRemaining - 1, visited, state, isInActivePage));
            }

            foreach (var customChild in customChildren)
            {
                if (state.NodeCount >= options.MaxNodes)
                {
                    state.MarkTruncated();
                    flags |= 64;
                    break;
                }

                try
                {
                    childNodes.Add(BuildCustomVisualTreeNode(
                        customChild,
                        options,
                        depthRemaining - 1,
                        state,
                        isInActivePage,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        customBuilderErrors));
                }
                catch (Exception exception)
                {
                    customBuilderErrors.Add(CreateCustomVisualTreeError(element, "builder", exception));
                }
            }

            json["children"] = childNodes;
        }

        if (customWalkerErrors.Count > 0 || customBuilderErrors.Count > 0)
        {
            var customVisualTreeErrors = new JsonArray();
            foreach (var customWalkerError in customWalkerErrors)
            {
                customVisualTreeErrors.Add(customWalkerError);
            }

            foreach (var customBuilderError in customBuilderErrors)
            {
                customVisualTreeErrors.Add(customBuilderError);
            }

            json["customVisualTreeErrors"] = customVisualTreeErrors;
        }

        json["flags"] = flags;
        visited.Remove(elementId);
        return json;
    }

    internal static bool IsElementDescendantOrSelf(Element root, Element candidate)
    {
        if (ReferenceEquals(root, candidate))
        {
            return true;
        }

        return TryFindElement(
            root,
            GetElementId(candidate),
            new List<Element>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            out _);
    }

    internal static IReadOnlyList<Element> GetChildElements(Element element)
    {
        return GetChildElements(element, MauiElementTraversalOptions.Full);
    }

    internal static IReadOnlyList<Element> GetChildElements(
        Element element,
        MauiElementTraversalOptions traversalOptions)
    {
        return GetChildElements(element, traversalOptions, customWalkerErrors: null);
    }

    internal static IReadOnlyList<Element> GetChildElements(
        Element element,
        MauiElementTraversalOptions traversalOptions,
        List<string>? customWalkerErrors)
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

        foreach (var property in GetChildElementProperties(element.GetType()))
        {
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

        AddShellNavigationElements(children, element);
        AddModalNavigationElements(children, element);
        AddRealizedItemsViewElements(children, element);
        AddCustomChildElements(children, element, traversalOptions, customWalkerErrors);

        children.RemoveAll(child => ReferenceEquals(child, element));
        if (!traversalOptions.IncludeInactiveNavigationChildren
            && TryGetActiveNavigationChild(element, out var activeChild)
            && activeChild != null)
        {
            children.RemoveAll(child => !ReferenceEquals(child, activeChild));
            AddDistinctElement(children, activeChild);
        }

        return children;
    }

    private static bool TryGetActiveNavigationChild(Element element, out Element? activeChild)
    {
        activeChild = null;
        if (element is not Page page)
        {
            return false;
        }

        var modalPage = GetModalStack(page).LastOrDefault(modal => !ReferenceEquals(modal, page));
        activeChild = modalPage ?? GetDisplayedNavigationChildPage(page);
        return activeChild != null;
    }

    private static PropertyInfo[] GetChildElementProperties(Type elementType)
    {
        return ChildElementPropertiesByType.GetOrAdd(elementType, static type =>
        {
            var properties = new List<PropertyInfo>(ChildElementPropertyNames.Length);
            foreach (var propertyName in ChildElementPropertyNames)
            {
                var property = type.GetRuntimeProperty(propertyName);
                if (property is not null && property.GetIndexParameters().Length == 0)
                {
                    properties.Add(property);
                }
            }

            return properties.ToArray();
        });
    }

    internal static void AddModalNavigationElements(List<Element> children, Element element)
    {
        if (element is not Page page)
        {
            return;
        }

        foreach (var modalPage in GetModalStack(page))
        {
            AddDistinctElement(children, modalPage);
        }
    }

    internal static void AddShellNavigationElements(List<Element> children, Element element)
    {
        switch (element)
        {
            case Shell shell:
                AddElementValue(children, shell.Items);
                return;
            case ShellItem shellItem:
                AddElementValue(children, shellItem.Items);
                return;
            case ShellSection shellSection:
                AddElementValue(children, shellSection.Items);
                return;
            case ShellContent shellContent when shellContent.Content is Element content:
                AddDistinctElement(children, content);
                return;
        }
    }

    internal static void AddRealizedItemsViewElements(List<Element> children, Element element)
    {
        if (element is not ItemsView itemsView)
        {
            return;
        }

        if (TryReadPublicProperty(itemsView, "VisibleViews", out var visibleViews, out _) &&
            visibleViews is IEnumerable visibleViewEnumerable)
        {
            AddElementValue(children, visibleViewEnumerable);
        }

        AddPlatformRealizedItemsViewElements(children, itemsView);
    }

    private static void AddCustomChildElements(
        List<Element> children,
        Element element,
        MauiElementTraversalOptions traversalOptions,
        List<string>? customWalkerErrors)
    {
        var registrations = MauiVisualTreeRegistry.GetRegistrations(element);
        if (registrations.Count == 0)
        {
            return;
        }

        var context = new MauiVisualTreeWalkContext(element, traversalOptions.IncludeInactiveNavigationChildren);
        foreach (var registration in registrations)
        {
            IEnumerable<Element>? customChildren;
            try
            {
                customChildren = registration.WalkChildren(element, context);
            }
            catch (Exception exception)
            {
                customWalkerErrors?.Add(CreateCustomVisualTreeError(element, "walker", exception));
                continue;
            }

            if (customChildren is null)
            {
                continue;
            }

            try
            {
                foreach (var customChild in customChildren)
                {
                    if (customChild is not null)
                    {
                        AddDistinctElement(children, customChild);
                    }
                }
            }
            catch (Exception exception)
            {
                customWalkerErrors?.Add(CreateCustomVisualTreeError(element, "walker", exception));
            }
        }
    }

    private static List<MauiVisualTreeNode> GetCustomVisualTreeChildNodes(
        Element element,
        MauiTreeBuildOptions options,
        int depthRemaining,
        List<string> customBuilderErrors)
    {
        var registrations = MauiVisualTreeRegistry.GetRegistrations(element);
        if (registrations.Count == 0 || depthRemaining < 0)
        {
            return [];
        }

        var context = new MauiVisualTreeBuildContext(
            element,
            options.IncludeBounds,
            options.IncludeProperties,
            options.IncludeBindableProperties,
            options.IncludeBindingContexts,
            options.TraversalOptions.IncludeInactiveNavigationChildren,
            depthRemaining,
            options.MaxNodes);
        var customNodes = new List<MauiVisualTreeNode>();
        foreach (var registration in registrations)
        {
            IEnumerable<MauiVisualTreeNode>? builtNodes;
            try
            {
                builtNodes = registration.BuildChildren(element, context);
            }
            catch (Exception exception)
            {
                customBuilderErrors.Add(CreateCustomVisualTreeError(element, "builder", exception));
                continue;
            }

            if (builtNodes is null)
            {
                continue;
            }

            try
            {
                foreach (var builtNode in builtNodes)
                {
                    if (builtNode is not null)
                    {
                        customNodes.Add(builtNode);
                    }
                }
            }
            catch (Exception exception)
            {
                customBuilderErrors.Add(CreateCustomVisualTreeError(element, "builder", exception));
            }
        }

        return customNodes;
    }

    private static JsonObject BuildCustomVisualTreeNode(
        MauiVisualTreeNode node,
        MauiTreeBuildOptions options,
        int depthRemaining,
        MauiTreeBuildState state,
        bool parentIsInActivePage,
        HashSet<string> visited,
        List<string> customBuilderErrors)
    {
        var json = CreateCompactCustomVisualTreeNodeReference(node, options.TypeRegistry);
        json["visual"] = CreateCustomVisual(node);
        var flags = 128;

        if (node.IsVisible ?? true)
        {
            flags |= 1;
        }

        if (node.IsEnabled ?? true)
        {
            flags |= 2;
        }

        if (parentIsInActivePage)
        {
            flags |= 8;
        }

        if (options.IncludeBounds && TryCreateCustomVisualTreeBounds(node, out var bounds))
        {
            json["bounds"] = bounds;
        }

        if (options.IncludeProperties && node.Properties is not null)
        {
            json["properties"] = node.Properties.DeepClone();
        }

        if (!state.TryIncludeNode())
        {
            flags |= 32;
            json["flags"] = flags;
            json["childCount"] = 0;
            return json;
        }

        if (!visited.Add(node.Id))
        {
            flags |= 16;
            json["flags"] = flags;
            json["childCount"] = 0;
            return json;
        }

        if (depthRemaining <= 0)
        {
            json["flags"] = flags;
            json["childCount"] = 0;
            visited.Remove(node.Id);
            return json;
        }

        var customChildren = node.Children.Where(child => child is not null).ToArray();
        json["childCount"] = customChildren.Length;
        if (customChildren.Length > 0)
        {
            var childNodes = new JsonArray();
            foreach (var customChild in customChildren)
            {
                if (state.NodeCount >= options.MaxNodes)
                {
                    state.MarkTruncated();
                    flags |= 64;
                    break;
                }

                try
                {
                    childNodes.Add(BuildCustomVisualTreeNode(
                        customChild,
                        options,
                        depthRemaining - 1,
                        state,
                        parentIsInActivePage,
                        visited,
                        customBuilderErrors));
                }
                catch (Exception exception)
                {
                    customBuilderErrors.Add($"Custom visual tree node '{node.Id}' child build failed: {exception.Message}");
                }
            }

            json["children"] = childNodes;
        }

        json["flags"] = flags;
        visited.Remove(node.Id);
        return json;
    }

    private static JsonObject CreateCompactCustomVisualTreeNodeReference(MauiVisualTreeNode node, MauiTypeRegistry typeRegistry)
    {
        var type = NormalizeCustomVisualTreeText(node.Type, "CustomVisualNode");
        var kind = NormalizeCustomVisualTreeText(node.Kind, "custom");
        var json = new JsonObject
        {
            ["id"] = node.Id,
            ["type"] = type,
            ["typeId"] = typeRegistry.GetTypeId(type),
            ["kind"] = kind,
            ["source"] = "custom",
            ["automationId"] = NullIfWhiteSpace(node.AutomationId),
            ["styleId"] = NullIfWhiteSpace(node.StyleId),
            ["classId"] = NullIfWhiteSpace(node.ClassId),
            ["label"] = CreateSafeLabel(node.Label)
        };

        if (!string.IsNullOrWhiteSpace(node.Title))
        {
            json["title"] = CreateSafeLabel(node.Title);
        }

        return json;
    }

    private static JsonObject CreateCustomVisual(MauiVisualTreeNode node)
    {
        var visual = new JsonObject
        {
            ["opacity"] = Math.Clamp(node.Opacity ?? 1d, 0d, 1d)
        };
        AddVisualString(visual, "foreground", node.ForegroundColor?.ToArgbHex());
        AddVisualString(visual, "background", node.BackgroundColor?.ToArgbHex());
        AddVisualString(visual, "text", CreateSafeLabel(node.Text));
        AddVisualString(visual, "value", CreateSafeLabel(node.Value));

        return visual;
    }

    private static bool TryCreateCustomVisualTreeBounds(MauiVisualTreeNode node, out JsonArray bounds)
    {
        bounds = new JsonArray();

        var localBounds = node.Bounds ?? node.AbsoluteBounds;
        if (localBounds is null)
        {
            return false;
        }

        bounds.Add(CreateCompactNumber(localBounds.Value.X));
        bounds.Add(CreateCompactNumber(localBounds.Value.Y));
        bounds.Add(CreateCompactNumber(localBounds.Value.Width));
        bounds.Add(CreateCompactNumber(localBounds.Value.Height));

        if (node.AbsoluteBounds is { } absoluteBounds)
        {
            bounds.Add(CreateCompactNumber(absoluteBounds.X));
            bounds.Add(CreateCompactNumber(absoluteBounds.Y));
            bounds.Add(CreateCompactNumber(absoluteBounds.Width));
            bounds.Add(CreateCompactNumber(absoluteBounds.Height));
        }

        return true;
    }

    private static string NormalizeCustomVisualTreeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : Truncate(value.Trim()) ?? fallback;
    }

    private static string CreateCustomVisualTreeError(Element element, string component, Exception exception)
    {
        return $"Custom visual tree {component} failed for {GetTypeDisplayName(element.GetType())} ({GetElementId(element)}): {exception.Message}";
    }

    private static void AddPlatformRealizedItemsViewElements(List<Element> children, ItemsView itemsView)
    {
#if ANDROID
        if (itemsView.Handler?.PlatformView is not RecyclerView recyclerView)
        {
            return;
        }

        for (var index = 0; index < recyclerView.ChildCount; index++)
        {
            var child = recyclerView.GetChildAt(index);
            if (child == null)
            {
                continue;
            }

            var viewHolder = recyclerView.GetChildViewHolder(child);
            if (TryGetHostedMauiElement(viewHolder, out var hostedElement) && hostedElement != null)
            {
                AddDistinctElement(children, hostedElement);
                continue;
            }

            if (TryGetHostedMauiElement(child, out hostedElement) && hostedElement != null)
            {
                AddDistinctElement(children, hostedElement);
            }
        }
#elif IOS || MACCATALYST
        if (itemsView.Handler?.PlatformView is not UICollectionView collectionView)
        {
            return;
        }

        foreach (var cell in collectionView.VisibleCells)
        {
            if (TryGetHostedMauiElement(cell, out var hostedElement) && hostedElement != null)
            {
                AddDistinctElement(children, hostedElement);
            }
        }
#endif
    }

    private static bool TryGetHostedMauiElement(object? nativeObject, out Element? element)
    {
        element = null;

        if (nativeObject == null)
        {
            return false;
        }

        if (TryReadInstanceProperty(nativeObject, "View", includeNonPublic: true, out var view) &&
            view is Element viewElement)
        {
            element = viewElement;
            return true;
        }

        if (TryReadInstanceProperty(nativeObject, "PlatformHandler", includeNonPublic: true, out var platformHandler) &&
            TryGetHandlerVirtualElement(platformHandler, out element))
        {
            return true;
        }

        if (TryReadInstanceProperty(nativeObject, "Content", includeNonPublic: true, out var contentHandler) &&
            TryGetHandlerVirtualElement(contentHandler, out element))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetHandlerVirtualElement(object? handler, out Element? element)
    {
        element = null;

        if (handler == null)
        {
            return false;
        }

        if (TryReadInstanceProperty(handler, "VirtualView", includeNonPublic: false, out var virtualView) &&
            virtualView is Element virtualElement)
        {
            element = virtualElement;
            return true;
        }

        return false;
    }

    private static bool TryReadInstanceProperty(object target, string propertyName, bool includeNonPublic, out object? value)
    {
        value = null;

        var flags = BindingFlags.Instance | BindingFlags.Public;
        if (includeNonPublic)
        {
            flags |= BindingFlags.NonPublic;
        }

        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(propertyName, flags | BindingFlags.DeclaredOnly);
            if (property == null || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            try
            {
                value = property.GetValue(target);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
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

    internal static JsonObject CreateCompactElementReference(Element element, MauiTypeRegistry typeRegistry)
    {
        var json = new JsonObject
        {
            ["id"] = GetElementId(element),
            ["type"] = GetTypeShortName(element.GetType()),
            ["typeId"] = typeRegistry.GetTypeId(element.GetType()),
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
        var x = visualElement.X;
        var y = visualElement.Y;
        var width = visualElement.Width;
        var height = visualElement.Height;
        var source = "layout";

        if (ShouldUsePlatformParentBounds(visualElement) &&
            TryCreateParentRelativePlatformBounds(visualElement, out var platformBounds))
        {
            x = platformBounds.X;
            y = platformBounds.Y;
            width = platformBounds.Width;
            height = platformBounds.Height;
            source = "platform";
        }

        var json = new JsonObject
        {
            ["x"] = x,
            ["y"] = y,
            ["width"] = width,
            ["height"] = height,
            ["source"] = source,
            ["layoutX"] = visualElement.X,
            ["layoutY"] = visualElement.Y,
            ["layoutWidth"] = visualElement.Width,
            ["layoutHeight"] = visualElement.Height
        };

        if (TryCreateWindowRelativePlatformBounds(visualElement, out var absoluteBounds))
        {
            json["absoluteX"] = absoluteBounds.X;
            json["absoluteY"] = absoluteBounds.Y;
            json["absoluteWidth"] = absoluteBounds.Width;
            json["absoluteHeight"] = absoluteBounds.Height;

            if (TryCreateVisibleWindowBounds(visualElement, absoluteBounds, out var visibleBounds))
            {
                json["visibleX"] = visibleBounds.Bounds.X;
                json["visibleY"] = visibleBounds.Bounds.Y;
                json["visibleWidth"] = visibleBounds.Bounds.Width;
                json["visibleHeight"] = visibleBounds.Bounds.Height;
                json["isOnScreen"] = visibleBounds.IsOnScreen;
                json["isClipped"] = visibleBounds.IsClipped;
                json["clipSource"] = visibleBounds.ClipSource;
                json["clipNodeId"] = visibleBounds.ClipNodeId;
            }
        }

        return json;
    }

    internal static JsonArray CreateCompactBoundsSnapshot(VisualElement visualElement)
    {
        var x = visualElement.X;
        var y = visualElement.Y;
        var width = visualElement.Width;
        var height = visualElement.Height;

        if (ShouldUsePlatformParentBounds(visualElement) &&
            TryCreateParentRelativePlatformBounds(visualElement, out var platformBounds))
        {
            x = platformBounds.X;
            y = platformBounds.Y;
            width = platformBounds.Width;
            height = platformBounds.Height;
        }

        var bounds = new JsonArray(
            CreateCompactNumber(x),
            CreateCompactNumber(y),
            CreateCompactNumber(width),
            CreateCompactNumber(height));

        if (TryCreateWindowRelativePlatformBounds(visualElement, out var absoluteBounds))
        {
            bounds.Add(CreateCompactNumber(absoluteBounds.X));
            bounds.Add(CreateCompactNumber(absoluteBounds.Y));
            bounds.Add(CreateCompactNumber(absoluteBounds.Width));
            bounds.Add(CreateCompactNumber(absoluteBounds.Height));
        }

        return bounds;
    }

    internal static JsonObject CreateScrollViewSnapshot(ScrollView scrollView)
    {
        var json = new JsonObject
        {
            ["scrollX"] = scrollView.ScrollX,
            ["scrollY"] = scrollView.ScrollY,
            ["viewportWidth"] = scrollView.Width,
            ["viewportHeight"] = scrollView.Height,
            ["orientation"] = scrollView.Orientation.ToString(),
            ["contentId"] = scrollView.Content == null ? null : GetElementId(scrollView.Content)
        };

        if (scrollView.Content is VisualElement content)
        {
            json["contentWidth"] = content.Width;
            json["contentHeight"] = content.Height;
        }

        if (TryReadPublicProperty(scrollView, "ContentSize", out var contentSize, out var contentSizeType))
        {
            json["contentSize"] = CreateValueSnapshot(contentSize, contentSizeType, depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
        }

        return json;
    }

    internal static JsonObject? CreateCoordinateSpaceSnapshot(Page? rootPage)
    {
        if (rootPage is not VisualElement visualElement)
        {
            return null;
        }

        if (TryCreateWindowRelativePlatformBounds(visualElement, out var bounds))
        {
            return new JsonObject
            {
                ["x"] = bounds.X,
                ["y"] = bounds.Y,
                ["width"] = bounds.Width,
                ["height"] = bounds.Height,
                ["source"] = "window"
            };
        }

        return new JsonObject
        {
            ["x"] = visualElement.X,
            ["y"] = visualElement.Y,
            ["width"] = visualElement.Width,
            ["height"] = visualElement.Height,
            ["source"] = "layout"
        };
    }

    private static bool ShouldUsePlatformParentBounds(VisualElement visualElement)
        => visualElement.Parent is ItemsView or ScrollView;

    private static bool TryCreateParentRelativePlatformBounds(VisualElement visualElement, out MauiPlatformBounds bounds)
    {
        bounds = default;

        if (visualElement.Parent is not VisualElement parent)
        {
            return false;
        }

#if ANDROID
        if (visualElement.Handler?.PlatformView is not AView platformView ||
            parent.Handler?.PlatformView is not AView parentPlatformView ||
            platformView.Width <= 0 ||
            platformView.Height <= 0)
        {
            return false;
        }

        var childLocation = new int[2];
        var parentLocation = new int[2];
        platformView.GetLocationOnScreen(childLocation);
        parentPlatformView.GetLocationOnScreen(parentLocation);

        var density = platformView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        if (density <= 0)
        {
            density = 1f;
        }

        bounds = new MauiPlatformBounds(
            (childLocation[0] - parentLocation[0]) / density,
            (childLocation[1] - parentLocation[1]) / density,
            platformView.Width / density,
            platformView.Height / density);
        return true;
#elif IOS || MACCATALYST
        if (visualElement.Handler?.PlatformView is not UIView platformView ||
            parent.Handler?.PlatformView is not UIView parentPlatformView ||
            platformView.Bounds.Width <= 0 ||
            platformView.Bounds.Height <= 0)
        {
            return false;
        }

        CGRect frame;
        try
        {
            frame = platformView.ConvertRectToView(platformView.Bounds, parentPlatformView);
        }
        catch
        {
            return false;
        }

        bounds = new MauiPlatformBounds(
            (double)frame.X,
            (double)frame.Y,
            (double)frame.Width,
            (double)frame.Height);
        return true;
#else
        return false;
#endif
    }

    private static bool TryCreateWindowRelativePlatformBounds(VisualElement visualElement, out MauiPlatformBounds bounds)
    {
        bounds = default;

#if ANDROID
        if (visualElement.Handler?.PlatformView is not AView platformView ||
            platformView.Width <= 0 ||
            platformView.Height <= 0)
        {
            return false;
        }

        var location = new int[2];
        var rootLocation = new int[2];
        platformView.GetLocationOnScreen(location);
        platformView.RootView?.GetLocationOnScreen(rootLocation);

        var density = platformView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        if (density <= 0)
        {
            density = 1f;
        }

        bounds = new MauiPlatformBounds(
            (location[0] - rootLocation[0]) / density,
            (location[1] - rootLocation[1]) / density,
            platformView.Width / density,
            platformView.Height / density);
        return true;
#elif IOS || MACCATALYST
        if (visualElement.Handler?.PlatformView is not UIView platformView ||
            platformView.Window == null ||
            platformView.Bounds.Width <= 0 ||
            platformView.Bounds.Height <= 0)
        {
            return false;
        }

        CGRect frame;
        try
        {
            frame = platformView.ConvertRectToView(platformView.Bounds, platformView.Window);
        }
        catch
        {
            return false;
        }

        bounds = new MauiPlatformBounds(
            (double)frame.X,
            (double)frame.Y,
            (double)frame.Width,
            (double)frame.Height);
        return true;
#else
        return false;
#endif
    }

    private static bool TryCreateVisibleWindowBounds(
        VisualElement visualElement,
        MauiPlatformBounds absoluteBounds,
        out MauiVisibleBounds visibleBounds)
    {
        var clippedBounds = absoluteBounds;
        var isClipped = false;
        string? clipSource = null;
        string? clipNodeId = null;

        if (TryCreateNativeWindowViewportBounds(visualElement, out var windowBounds))
        {
            ApplyClip(windowBounds, "window", null);
        }

        foreach (var scrollView in GetAncestorScrollViews(visualElement))
        {
            if (!TryCreateWindowRelativePlatformBounds(scrollView, out var scrollViewBounds))
            {
                continue;
            }

            ApplyClip(scrollViewBounds, "scrollView", GetElementId(scrollView));
        }

        var isOnScreen = clippedBounds.Width > 0 &&
                         clippedBounds.Height > 0 &&
                         IsVisibleThroughAncestors(visualElement);
        visibleBounds = new MauiVisibleBounds(clippedBounds, isOnScreen, isClipped, clipSource, clipNodeId);
        return true;

        void ApplyClip(MauiPlatformBounds clipBounds, string source, string? nodeId)
        {
            var intersectedBounds = IntersectBounds(clippedBounds, clipBounds);
            if (!BoundsAreEqual(intersectedBounds, clippedBounds))
            {
                isClipped = true;
                clipSource ??= source;
                clipNodeId ??= nodeId;
                clippedBounds = intersectedBounds;
            }
        }
    }

    private static IEnumerable<ScrollView> GetAncestorScrollViews(VisualElement visualElement)
    {
        for (var parent = visualElement.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ScrollView scrollView)
            {
                yield return scrollView;
            }
        }
    }

    private static bool IsVisibleThroughAncestors(VisualElement visualElement)
    {
        for (Element? element = visualElement; element != null; element = element.Parent)
        {
            if (element is VisualElement ancestor && (!ancestor.IsVisible || ancestor.Opacity <= 0))
            {
                return false;
            }
        }

        return true;
    }

    private static MauiPlatformBounds IntersectBounds(MauiPlatformBounds first, MauiPlatformBounds second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);

        return new MauiPlatformBounds(
            left,
            top,
            Math.Max(0, right - left),
            Math.Max(0, bottom - top));
    }

    private static bool BoundsAreEqual(MauiPlatformBounds first, MauiPlatformBounds second)
        => Math.Abs(first.X - second.X) < double.Epsilon &&
           Math.Abs(first.Y - second.Y) < double.Epsilon &&
           Math.Abs(first.Width - second.Width) < double.Epsilon &&
           Math.Abs(first.Height - second.Height) < double.Epsilon;

    private static bool TryCreateNativeWindowViewportBounds(VisualElement visualElement, out MauiPlatformBounds bounds)
    {
        bounds = default;

#if ANDROID
        if (visualElement.Handler?.PlatformView is not AView platformView ||
            platformView.RootView is not { } rootView ||
            rootView.Width <= 0 ||
            rootView.Height <= 0)
        {
            return false;
        }

        var density = platformView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        if (density <= 0)
        {
            density = 1f;
        }

        bounds = new MauiPlatformBounds(
            0,
            0,
            rootView.Width / density,
            rootView.Height / density);
        return true;
#elif IOS || MACCATALYST
        if (visualElement.Handler?.PlatformView is not UIView { Window: { } window })
        {
            return false;
        }

        bounds = new MauiPlatformBounds(
            0,
            0,
            (double)window.Bounds.Width,
            (double)window.Bounds.Height);
        return true;
#else
        return false;
#endif
    }

    internal static JsonObject CreateElementProperties(Element element)
        => CreateElementProperties(element, typeRegistry: null);

    internal static JsonObject CreateElementProperties(Element element, MauiTypeRegistry? typeRegistry)
    {
        var properties = new JsonObject
        {
            ["parentId"] = element.Parent == null ? null : GetElementId(element.Parent)
        };

        if (element.BindingContext != null)
        {
            if (typeRegistry == null)
            {
                properties["bindingContextType"] = CreateTypeMetadata(element.BindingContext.GetType());
            }
            else
            {
                properties["bindingContextTypeId"] = typeRegistry.GetTypeId(element.BindingContext.GetType());
            }
        }

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

        if (element is ScrollView scrollView)
        {
            properties["scroll"] = CreateScrollViewSnapshot(scrollView);
        }

        return properties;
    }

    internal static JsonObject CreateElementVisual(Element element)
    {
        var visual = new JsonObject
        {
            ["opacity"] = element is VisualElement visualElement
                ? Math.Clamp(visualElement.Opacity, 0d, 1d)
                : 1d
        };

        AddVisualString(visual, "foreground", GetForegroundColor(element)?.ToArgbHex());
        AddVisualString(visual, "background", GetBackgroundColor(element)?.ToArgbHex());
        AddVisualString(visual, "text", GetElementLabel(element));
        if (GetElementValue(element) is { } value)
        {
            visual["value"] = value;
        }

        return visual;
    }

    private static Color? GetForegroundColor(Element element)
    {
        return element switch
        {
            Label label => label.TextColor,
            Button button => button.TextColor,
            InputView inputView => inputView.TextColor,
            Picker picker => picker.TextColor,
            DatePicker datePicker => datePicker.TextColor,
            TimePicker timePicker => timePicker.TextColor,
            CheckBox checkBox => checkBox.Color,
            _ => null
        };
    }

    private static Color? GetBackgroundColor(Element element)
    {
        if (element is VisualElement { Background: SolidColorBrush brush })
        {
            return brush.Color;
        }

        return element is Page page ? page.BackgroundColor : null;
    }

    private static string? GetElementValue(Element element)
    {
        return element switch
        {
            Entry { IsPassword: false } entry => CreateVisualStringValue(entry.Text),
            Editor editor => CreateVisualStringValue(editor.Text),
            SearchBar searchBar => CreateVisualStringValue(searchBar.Text),
            Picker { SelectedItem: not null } picker => CreateVisualStringValue(picker.SelectedItem.ToString()),
            DatePicker datePicker => datePicker.Date.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            TimePicker timePicker => timePicker.Time.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            CheckBox checkBox => checkBox.IsChecked ? "true" : "false",
            Switch toggle => toggle.IsToggled ? "true" : "false",
            Slider slider => CreateCompactNumber(slider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Stepper stepper => CreateCompactNumber(stepper.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string? CreateVisualStringValue(string? value)
        => CreateSafeLabel(value);

    private static void AddVisualString(JsonObject visual, string propertyName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            visual[propertyName] = value;
        }
    }

    internal static string GetElementKind(Element element)
    {
        return element switch
        {
            Window => "window",
            Shell => "shell",
            FlyoutPage => "flyoutPage",
            TabbedPage => "tabbedPage",
            NavigationPage => "navigationPage",
            ShellItem => "shellItem",
            ShellSection => "shellSection",
            ShellContent => "shellContent",
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
            BaseShellItem shellItem => CreateSafeLabel(shellItem.Title),
            MenuItem menuItem => CreateSafeLabel(menuItem.Text),
            _ => null
        };

        return label;
    }

    internal static string GetElementId(Element element) => element.Id.ToString("N");

    internal static int ReadInt(JsonObject jsonObject, string propertyName)
    {
        if (jsonObject[propertyName] is JsonValue value && value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        return 0;
    }

    internal static double CreateCompactNumber(double value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
#endif
