namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

public static class MauiVisualTreeRegistry
{
    private static readonly object syncRoot = new();
    private static readonly List<IMauiVisualTreeRegistration> registrations = [];

    public static IDisposable Register<TElement>(MauiVisualTreeRegistration<TElement> registration)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (registration.WalkChildren is null && registration.BuildChildren is null)
        {
            throw new ArgumentException("A visual tree registration must provide a child walker or a child builder.", nameof(registration));
        }

        var adapter = new MauiVisualTreeRegistrationAdapter<TElement>(registration);
        lock (syncRoot)
        {
            registrations.Add(adapter);
        }

        return new RegistrationHandle(adapter);
    }

    public static IDisposable RegisterChildWalker<TElement>(Func<TElement, MauiVisualTreeWalkContext, IEnumerable<Element>?> walker)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(walker);

        return Register(new MauiVisualTreeRegistration<TElement>
        {
            WalkChildren = walker
        });
    }

    public static IDisposable RegisterChildBuilder<TElement>(Func<TElement, MauiVisualTreeBuildContext, IEnumerable<MauiVisualTreeNode>?> builder)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(builder);

        return Register(new MauiVisualTreeRegistration<TElement>
        {
            BuildChildren = builder
        });
    }

    internal static IReadOnlyList<IMauiVisualTreeRegistration> GetRegistrations(Element element)
    {
        lock (syncRoot)
        {
            return registrations
                .Where(registration => registration.CanHandle(element))
                .ToArray();
        }
    }

    private static void Unregister(IMauiVisualTreeRegistration registration)
    {
        lock (syncRoot)
        {
            registrations.Remove(registration);
        }
    }

    private sealed class RegistrationHandle(IMauiVisualTreeRegistration registration) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Unregister(registration);
        }
    }
}

public sealed class MauiVisualTreeRegistration<TElement>
    where TElement : Element
{
    public Func<TElement, MauiVisualTreeWalkContext, IEnumerable<Element>?>? WalkChildren { get; init; }

    public Func<TElement, MauiVisualTreeBuildContext, IEnumerable<MauiVisualTreeNode>?>? BuildChildren { get; init; }
}

public sealed class MauiVisualTreeWalkContext
{
    internal MauiVisualTreeWalkContext(Element owner, bool includeInactiveNavigationChildren)
    {
        Owner = owner;
        OwnerNodeId = MauiToolHelpers.GetElementId(owner);
        IncludeInactiveNavigationChildren = includeInactiveNavigationChildren;
    }

    public Element Owner { get; }

    public string OwnerNodeId { get; }

    public bool IncludeInactiveNavigationChildren { get; }
}

public sealed class MauiVisualTreeBuildContext
{
    internal MauiVisualTreeBuildContext(
        Element owner,
        bool includeBounds,
        bool includeProperties,
        bool includeBindableProperties,
        bool includeBindingContexts,
        bool includeInactiveNavigationChildren,
        int depthRemaining,
        int maxNodes)
    {
        Owner = owner;
        OwnerNodeId = MauiToolHelpers.GetElementId(owner);
        IncludeBounds = includeBounds;
        IncludeProperties = includeProperties;
        IncludeBindableProperties = includeBindableProperties;
        IncludeBindingContexts = includeBindingContexts;
        IncludeInactiveNavigationChildren = includeInactiveNavigationChildren;
        DepthRemaining = depthRemaining;
        MaxNodes = maxNodes;
    }

    public Element Owner { get; }

    public string OwnerNodeId { get; }

    public bool IncludeBounds { get; }

    public bool IncludeProperties { get; }

    public bool IncludeBindableProperties { get; }

    public bool IncludeBindingContexts { get; }

    public bool IncludeInactiveNavigationChildren { get; }

    public int DepthRemaining { get; }

    public int MaxNodes { get; }

    public string CreateChildId(string childKey)
    {
        var normalizedChildKey = string.IsNullOrWhiteSpace(childKey)
            ? "custom"
            : childKey.Trim();
        return $"{OwnerNodeId}::custom::{normalizedChildKey}";
    }
}

public sealed class MauiVisualTreeNode
{
    public MauiVisualTreeNode(string id, string type, string kind)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A custom visual tree node id is required.", nameof(id)) : id.Trim();
        Type = string.IsNullOrWhiteSpace(type) ? "CustomVisualNode" : type.Trim();
        Kind = string.IsNullOrWhiteSpace(kind) ? "custom" : kind.Trim();
    }

    public string Id { get; }

    public string Type { get; set; }

    public string Kind { get; set; }

    public string? AutomationId { get; set; }

    public string? StyleId { get; set; }

    public string? ClassId { get; set; }

    public string? Label { get; set; }

    public string? Title { get; set; }

    public Rect? Bounds { get; set; }

    public Rect? AbsoluteBounds { get; set; }

    public bool? IsVisible { get; set; }

    public bool? IsEnabled { get; set; }

    public JsonObject? Properties { get; set; }

    public IList<MauiVisualTreeNode> Children { get; } = [];
}

internal interface IMauiVisualTreeRegistration
{
    bool CanHandle(Element element);

    IEnumerable<Element>? WalkChildren(Element element, MauiVisualTreeWalkContext context);

    IEnumerable<MauiVisualTreeNode>? BuildChildren(Element element, MauiVisualTreeBuildContext context);
}

internal sealed class MauiVisualTreeRegistrationAdapter<TElement>(MauiVisualTreeRegistration<TElement> registration) : IMauiVisualTreeRegistration
    where TElement : Element
{
    public bool CanHandle(Element element)
        => element is TElement;

    public IEnumerable<Element>? WalkChildren(Element element, MauiVisualTreeWalkContext context)
        => element is TElement typedElement && registration.WalkChildren is not null
            ? registration.WalkChildren(typedElement, context)
            : null;

    public IEnumerable<MauiVisualTreeNode>? BuildChildren(Element element, MauiVisualTreeBuildContext context)
        => element is TElement typedElement && registration.BuildChildren is not null
            ? registration.BuildChildren(typedElement, context)
            : null;
}
#endif
