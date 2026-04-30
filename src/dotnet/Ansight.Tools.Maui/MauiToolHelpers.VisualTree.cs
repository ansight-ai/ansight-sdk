namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Collections;
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
                json["bounds"] = CreateBoundsSnapshot(visualElement);
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

        AddModalNavigationElements(children, element);
        AddRealizedItemsViewElements(children, element);

        return children.Where(child => !ReferenceEquals(child, element)).ToArray();
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

        if (IsRealizedItemsViewChild(visualElement) &&
            TryCreateParentRelativePlatformBounds(visualElement, out var platformBounds))
        {
            x = platformBounds.X;
            y = platformBounds.Y;
            width = platformBounds.Width;
            height = platformBounds.Height;
            source = "platform";
        }

        return new JsonObject
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
    }

    private static bool IsRealizedItemsViewChild(VisualElement visualElement)
        => visualElement.Parent is ItemsView;

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
}
#endif
