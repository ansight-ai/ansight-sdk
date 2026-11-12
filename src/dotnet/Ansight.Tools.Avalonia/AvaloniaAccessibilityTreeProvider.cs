using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Ansight.Tools.VisualTree;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Ansight.Tools.Avalonia;

public sealed class AvaloniaAccessibilityTreeProvider : IVisualTreeProvider, IVisualTreeInteractionProvider
{
    private readonly AvaloniaInspectionSession session;
    private readonly ConditionalWeakTable<AutomationPeer, PeerIdentity> identities = new();
    private long nextPeerId;

    public AvaloniaAccessibilityTreeProvider(AvaloniaInspectionSession session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public string Source => "avalonia-accessibility";
    public string DisplayName => "Avalonia accessibility tree";

    public Task<ToolResult> GetVisualTreeAsync(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return AvaloniaInspectionSession.OnUiThreadAsync(() => Capture(arguments));
    }

    public Task<ToolResult> InspectNodeAsync(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return AvaloniaInspectionSession.OnUiThreadAsync(() =>
        {
            var peer = ResolvePeer(AvaloniaToolArguments.Required(arguments, "nodeId"));
            var state = new CaptureState(1);
            var node = BuildNode(peer, 0, state);

            return ToolResult.Success(new JsonObject
            {
                ["source"] = Source,
                ["node"] = node,
                ["types"] = state.Types
            });
        });
    }

    public Task<ToolResult> PerformActionAsync(VisualTreeActionRequest request, CancellationToken cancellationToken)
    {
        return AvaloniaInspectionSession.OnUiThreadAsync(() =>
        {
            var peer = ResolvePeer(request.NodeId);

            return peer is ControlAutomationPeer controlPeer
                ? AvaloniaElementActions.Perform(controlPeer.Owner, request)
                : ToolResult.Failure("This virtual automation peer has no supported Ansight actions.", "avalonia_action_not_supported");
        }, cancellationToken);
    }

    private ToolResult Capture(IReadOnlyDictionary<string, string> arguments)
    {
        var maxNodes = AvaloniaToolArguments.Integer(arguments, "maxNodes", 2000, 1, 100_000);
        var maxDepth = Math.Min(AvaloniaToolArguments.Integer(arguments, "maxDepth", 24, 0, 64), 24);
        var rootId = AvaloniaToolArguments.Get(arguments, "rootNodeId");
        var captureRoots = rootId is null
            ? session.GetRoots().Select(topLevel => new PeerRoot(ControlAutomationPeer.CreatePeerForElement(topLevel), topLevel)).ToArray()
            : new[] { ResolvePeerRoot(rootId) };
        var roots = captureRoots.Select(captureRoot => captureRoot.Peer).ToArray();

        if (roots.Length == 0)
        {
            return ToolResult.Failure("No Avalonia root is attached.", "avalonia_root_unavailable");
        }

        var state = new CaptureState(maxNodes);
        JsonObject root;

        if (roots.Length == 1)
        {
            state.Visited.Add(roots[0]);
            root = BuildNode(roots[0], maxDepth, state);
        }
        else
        {
            state.Count = 1;
            root = new JsonObject
            {
                ["id"] = "avalonia-ax:application",
                ["typeId"] = state.TypeId("Avalonia.Application"),
                ["role"] = "application",
                ["childCount"] = roots.Length,
                ["children"] = BuildChildren(roots, maxDepth, state)
            };
        }

        var topLevel = captureRoots.Length == 1 ? captureRoots[0].TopLevel : null;

        return ToolResult.Success(new JsonObject
        {
            ["format"] = "ansight.avalonia.accessibility-tree.compact.v2",
            ["platform"] = AvaloniaTreeProvider.PlatformName,
            ["source"] = Source,
            ["treeKind"] = "accessibility",
            ["coordinateSpace"] = AvaloniaCoordinateSpace.Capture(topLevel, session),
            ["hierarchy"] = "Avalonia.Automation.Peers.AutomationPeer.GetChildren",
            ["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["effectiveMaxDepth"] = maxDepth,
            ["types"] = state.Types,
            ["root"] = root,
            ["nodeCount"] = state.Count,
            ["truncated"] = state.Truncated
        });
    }

    private JsonObject BuildNode(AutomationPeer peer, int depth, CaptureState state)
    {
        state.Count++;
        var node = new JsonObject
        {
            ["id"] = GetId(peer),
            ["typeId"] = state.TypeId(peer.GetType().FullName ?? peer.GetType().Name)
        };

        try
        {
            AvaloniaAccessibility.Populate(node, peer);
            var bounds = peer.GetBoundingRectangle();
            node["bounds"] = new JsonObject
            {
                ["x"] = bounds.X,
                ["y"] = bounds.Y,
                ["width"] = bounds.Width,
                ["height"] = bounds.Height
            };
            node["coordinateSpace"] = "root-logical-pixels";

            if (peer is ControlAutomationPeer controlPeer && TopLevel.GetTopLevel(controlPeer.Owner) is { } topLevel)
            {
                node["rootId"] = session.GetNodeId(topLevel);
                node["renderScaling"] = topLevel.RenderScaling;
            }

            var children = peer.GetChildren();
            node["childCount"] = children.Count;
            node["children"] = BuildChildren(children, depth, state);
            node["truncated"] = children.Count > node["children"]!.AsArray().Count;
        }
        catch (Exception exception) when (exception is NotImplementedException or NotSupportedException)
        {
            // Native interop peers deliberately do not implement the framework peer contract.
            node["captureError"] = "automation_peer_not_supported";
            node["supportedActions"] = new JsonArray();
            node["children"] = new JsonArray();
            node["truncated"] = true;
            state.Truncated = true;
        }

        return node;
    }

    private JsonArray BuildChildren(IReadOnlyList<AutomationPeer> children, int depth, CaptureState state)
    {
        var result = new JsonArray();

        foreach (var child in children)
        {
            var budgetExhausted = depth == 0 || state.Count >= state.MaximumNodes;

            if (budgetExhausted || !state.Visited.Add(child))
            {
                state.Truncated = true;
                break;
            }

            result.Add(BuildNode(child, depth - 1, state));
        }

        return result;
    }

    private string GetId(AutomationPeer peer)
    {
        return peer is ControlAutomationPeer controlPeer
            ? session.GetNodeId(controlPeer.Owner)
            : identities.GetValue(peer, _ => new PeerIdentity($"avalonia-ax:{Interlocked.Increment(ref nextPeerId)}")).Id;
    }

    private AutomationPeer ResolvePeer(string id)
    {
        return ResolvePeerRoot(id).Peer;
    }

    private PeerRoot ResolvePeerRoot(string id)
    {
        var pending = new Stack<PeerRoot>(session.GetRoots()
            .Select(topLevel => new PeerRoot(ControlAutomationPeer.CreatePeerForElement(topLevel), topLevel)));
        var visited = new HashSet<AutomationPeer>();

        while (pending.TryPop(out var candidate) && visited.Count < 100_000)
        {
            var peer = candidate.Peer;

            if (!visited.Add(peer))
            {
                continue;
            }

            if (GetId(peer) == id)
            {
                return candidate;
            }

            try
            {
                foreach (var child in peer.GetChildren())
                {
                    pending.Push(new PeerRoot(child, candidate.TopLevel));
                }
            }
            catch (Exception exception) when (exception is NotImplementedException or NotSupportedException)
            {
                // Native interop descendants must be inspected with the platform accessibility source.
            }
        }

        throw new KeyNotFoundException($"Avalonia automation peer '{id}' is no longer attached to a registered root.");
    }

    private sealed record PeerIdentity(string Id);

    private sealed record PeerRoot(AutomationPeer Peer, TopLevel TopLevel);

    private sealed class CaptureState
    {
        private readonly Dictionary<string, int> typeIds = new(StringComparer.Ordinal);

        internal CaptureState(int maximumNodes)
        {
            MaximumNodes = maximumNodes;
        }

        internal int MaximumNodes { get; }
        internal int Count { get; set; }
        internal bool Truncated { get; set; }
        internal JsonArray Types { get; } = new();
        internal HashSet<AutomationPeer> Visited { get; } = new();

        internal int TypeId(string type)
        {
            if (!typeIds.TryGetValue(type, out var id))
            {
                id = Types.Count;
                Types.Add(type);
                typeIds.Add(type, id);
            }

            return id;
        }
    }
}
