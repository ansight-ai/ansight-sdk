namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;

internal static partial class MauiToolHelpers
{
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

    internal readonly record struct MauiPlatformBounds(
        double X,
        double Y,
        double Width,
        double Height);

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
