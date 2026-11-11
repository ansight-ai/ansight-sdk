using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Ansight.Tools.Avalonia;

public sealed class AvaloniaInspectionSession : IDisposable
{
    private readonly ConditionalWeakTable<StyledElement, NodeIdentity> identities = new();
    private readonly List<WeakReference<TopLevel>> registeredRoots = [];
    private long nextNodeId;
    private IDisposable? loadedSubscription;

    public static AvaloniaInspectionSession Default { get; } = new AvaloniaInspectionSession();

    public void StartTrackingRoots()
    {
        Dispatcher.UIThread.VerifyAccess();

        loadedSubscription ??= Control.LoadedEvent.AddClassHandler<Control>((control, _) =>
        {
            if (TopLevel.GetTopLevel(control) is { } root)
            {
                RegisterRoot(root);
            }
        });
    }

    public void RegisterRoot(TopLevel root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Dispatcher.UIThread.VerifyAccess();

        if (registeredRoots.Any(reference => reference.TryGetTarget(out var existing) && ReferenceEquals(existing, root)))
        {
            return;
        }

        registeredRoots.Add(new WeakReference<TopLevel>(root));
        root.Closed += RootClosed;
    }

    public IReadOnlyList<TopLevel> GetRoots()
    {
        Dispatcher.UIThread.VerifyAccess();

        var roots = new List<TopLevel>();
        registeredRoots.RemoveAll(reference => !reference.TryGetTarget(out _));

        foreach (var reference in registeredRoots)
        {
            if (reference.TryGetTarget(out var root))
            {
                roots.Add(root);
            }
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            roots.AddRange(desktop.Windows);
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var topLevel = TopLevel.GetTopLevel(singleView.MainView);

            if (topLevel is not null)
            {
                roots.Add(topLevel);
            }
        }

        return roots.Distinct().ToArray();
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        loadedSubscription?.Dispose();
        loadedSubscription = null;

        foreach (var reference in registeredRoots)
        {
            if (reference.TryGetTarget(out var root))
            {
                root.Closed -= RootClosed;
            }
        }

        registeredRoots.Clear();
    }

    private void RootClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is TopLevel root)
        {
            root.Closed -= RootClosed;
            registeredRoots.RemoveAll(reference => !reference.TryGetTarget(out var existing) || ReferenceEquals(existing, root));
        }
    }

    public string GetNodeId(StyledElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return identities.GetValue(element, _ => new NodeIdentity($"avalonia:{Interlocked.Increment(ref nextNodeId)}")).Id;
    }

    internal StyledElement ResolveNode(string nodeId)
    {
        Dispatcher.UIThread.VerifyAccess();

        foreach (var root in GetRoots())
        {
            foreach (var element in Enumerate(root, AvaloniaTreeKind.Visual).Concat(Enumerate(root, AvaloniaTreeKind.Logical)))
            {
                if (GetNodeId(element) == nodeId)
                {
                    return element;
                }
            }
        }

        throw new KeyNotFoundException($"Avalonia node '{nodeId}' is no longer attached to a registered root.");
    }

    internal static IReadOnlyList<StyledElement> GetChildren(StyledElement element, AvaloniaTreeKind treeKind)
    {
        if (treeKind == AvaloniaTreeKind.Visual && element is Visual visual)
        {
            return visual.GetVisualChildren().OfType<StyledElement>().ToArray();
        }

        if (treeKind == AvaloniaTreeKind.Logical && element is ILogical logical)
        {
            return logical.GetLogicalChildren().OfType<StyledElement>().ToArray();
        }

        return [];
    }

    internal static IEnumerable<StyledElement> Enumerate(StyledElement root, AvaloniaTreeKind treeKind)
    {
        var pending = new Stack<StyledElement>();
        var visited = new HashSet<StyledElement>(ReferenceEqualityComparer.Instance);
        pending.Push(root);

        while (pending.TryPop(out var element))
        {
            if (!visited.Add(element))
            {
                continue;
            }

            yield return element;

            foreach (var child in GetChildren(element, treeKind).Reverse())
            {
                pending.Push(child);
            }
        }
    }

    internal static async Task<ToolResult> OnUiThreadAsync(Func<ToolResult> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                return action();
            });
        }
        catch (KeyNotFoundException exception)
        {
            return ToolResult.Failure(exception.Message, "avalonia_node_not_found");
        }
        catch (ArgumentException exception)
        {
            return ToolResult.Failure(exception.Message, "avalonia_invalid_argument");
        }
        catch (InvalidOperationException exception)
        {
            return ToolResult.Failure(exception.Message, "avalonia_operation_unavailable");
        }
    }

    private sealed record NodeIdentity(string Id);
}
