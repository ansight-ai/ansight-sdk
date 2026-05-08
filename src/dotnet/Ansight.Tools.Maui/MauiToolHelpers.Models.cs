namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls;

internal static partial class MauiToolHelpers
{
    internal sealed record MauiTreeBuildOptions(
        bool IncludeBounds,
        bool IncludeProperties,
        bool IncludeBindableProperties,
        bool IncludeBindingContexts,
        int MaxNodes,
        string? CurrentPageId,
        MauiTypeRegistry TypeRegistry);

    internal sealed class MauiTreeBuildState(int maxNodes)
    {
        public int NodeCount { get; private set; }

        public bool Truncated { get; private set; }

        public bool TryIncludeNode()
        {
            if (NodeCount >= maxNodes)
            {
                Truncated = true;
                return false;
            }

            NodeCount++;
            return true;
        }

        public void MarkTruncated()
        {
            Truncated = true;
        }
    }

    internal sealed record MauiActiveRootContext(
        Window Window,
        Page? RootPage,
        Page? CurrentPage,
        Element Root,
        string NormalizedRootScope);

    internal sealed class MauiTypeRegistry
    {
        private readonly Dictionary<string, int> idsByTypeName = new(StringComparer.Ordinal);
        private readonly List<string> typeNames = [];

        public int GetTypeId(Type type)
        {
            var typeName = GetTypeDisplayName(type);
            if (idsByTypeName.TryGetValue(typeName, out var id))
            {
                return id;
            }

            id = typeNames.Count;
            typeNames.Add(typeName);
            idsByTypeName[typeName] = id;
            return id;
        }

        public JsonArray ToJson()
        {
            var types = new JsonArray();
            foreach (var typeName in typeNames)
            {
                types.Add(typeName);
            }

            return types;
        }
    }

    internal sealed record MauiElementResolution(
        Element Root,
        Element Element,
        IReadOnlyList<Element> Ancestors);

    internal sealed record MauiElementTraversalEntry(
        Element Element,
        IReadOnlyList<Element> Ancestors,
        int Depth);

    internal readonly record struct MauiPlatformBounds(
        double X,
        double Y,
        double Width,
        double Height);

    internal readonly record struct MauiVisibleBounds(
        MauiPlatformBounds Bounds,
        bool IsOnScreen,
        bool IsClipped,
        string? ClipSource,
        string? ClipNodeId);

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
}
#endif
