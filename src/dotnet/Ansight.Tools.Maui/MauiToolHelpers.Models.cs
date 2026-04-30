namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;

internal static partial class MauiToolHelpers
{
    internal sealed record MauiTreeBuildOptions(
        bool IncludeBounds,
        bool IncludeProperties,
        bool IncludeBindableProperties,
        bool IncludeBindingContexts,
        int MaxNodes,
        string? CurrentPageId);

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
