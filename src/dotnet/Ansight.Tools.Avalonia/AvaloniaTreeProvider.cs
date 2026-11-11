using System.Text.Json.Nodes;
using Ansight.Tools.VisualTree;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;

namespace Ansight.Tools.Avalonia;

public sealed class AvaloniaTreeProvider : IVisualTreeProvider, IVisualTreeInteractionProvider
{
    private readonly AvaloniaInspectionSession session;
    private readonly AvaloniaTreeKind treeKind;

    public AvaloniaTreeProvider(AvaloniaInspectionSession session, AvaloniaTreeKind treeKind)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.treeKind = treeKind;
    }

    public string Source => treeKind == AvaloniaTreeKind.Logical ? "avalonia-logical" : "avalonia-visual";

    public string DisplayName => treeKind == AvaloniaTreeKind.Logical ? "Avalonia logical tree" : "Avalonia visual tree";

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
            var element = session.ResolveNode(AvaloniaToolArguments.Required(arguments, "nodeId"));
            var state = new TreeBuildState(1);
            var node = BuildNode(element, 0, state);

            return ToolResult.Success(new JsonObject
            {
                ["source"] = Source,
                ["node"] = node,
                ["types"] = state.Types,
                ["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
            });
        });
    }

    public Task<ToolResult> PerformActionAsync(VisualTreeActionRequest request, CancellationToken cancellationToken)
    {
        return AvaloniaInspectionSession.OnUiThreadAsync(
            () => AvaloniaElementActions.Perform(session.ResolveNode(request.NodeId), request),
            cancellationToken);
    }

    private ToolResult Capture(IReadOnlyDictionary<string, string> arguments)
    {
        var maxNodes = AvaloniaToolArguments.Integer(arguments, "maxNodes", 2000, 1, 100_000);
        var maxDepth = AvaloniaToolArguments.Integer(arguments, "maxDepth", 24, 0, 64);
        // Each control adds an object and a children array to the wire envelope.
        // Leave room for the session/tool envelope within the protocol's JSON depth limit.
        var effectiveMaxDepth = Math.Min(maxDepth, 24);
        var rootNodeId = AvaloniaToolArguments.Get(arguments, "rootNodeId");
        var roots = rootNodeId is null
            ? session.GetRoots().Cast<StyledElement>().ToArray()
            : new[] { session.ResolveNode(rootNodeId) };

        if (roots.Length == 0)
        {
            return ToolResult.Failure("No Avalonia root is attached. Register the application's TopLevel after initialization.", "avalonia_root_unavailable");
        }

        var state = new TreeBuildState(maxNodes);
        var multipleRoots = roots.Length > 1;

        if (multipleRoots)
        {
            state.NodeCount = 1;
        }

        var rootNodes = new JsonArray();

        foreach (var root in roots)
        {
            if (state.NodeCount >= maxNodes)
            {
                state.Truncated = true;
                break;
            }

            rootNodes.Add((JsonNode)BuildNode(root, effectiveMaxDepth, state));
        }

        JsonNode rootNode;

        if (!multipleRoots)
        {
            rootNode = rootNodes[0]!.DeepClone();
        }
        else
        {
            rootNode = new JsonObject
            {
                ["id"] = "avalonia:application",
                ["typeId"] = state.TypeId("Avalonia.Application"),
                ["flags"] = state.Truncated ? 35 : 3,
                ["childCount"] = roots.Length,
                ["children"] = rootNodes
            };
        }

        return ToolResult.Success(new JsonObject
        {
            ["format"] = "ansight.avalonia.visual-tree.compact.v2",
            ["platform"] = PlatformName,
            ["source"] = Source,
            ["treeKind"] = treeKind.ToString().ToLowerInvariant(),
            ["coordinateSystem"] = "root-logical-pixels",
            ["effectiveMaxDepth"] = effectiveMaxDepth,
            ["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["boundsColumns"] = new JsonArray("x", "y", "width", "height", "absoluteX", "absoluteY", "absoluteWidth", "absoluteHeight"),
            ["flagBits"] = new JsonObject { ["visible"] = 1, ["enabled"] = 2, ["focused"] = 4, ["truncated"] = 32 },
            ["types"] = state.Types,
            ["root"] = rootNode,
            ["nodeCount"] = state.NodeCount,
            ["truncated"] = state.Truncated
        });
    }

    private JsonObject BuildNode(StyledElement element, int remainingDepth, TreeBuildState state)
    {
        state.NodeCount++;

        var children = AvaloniaInspectionSession.GetChildren(element, treeKind);
        var visual = element as Visual;
        var input = element as InputElement;
        var flags = visual?.IsEffectivelyVisible == false ? 0 : 1;

        if (input?.IsEffectivelyEnabled != false)
        {
            flags |= 2;
        }

        if (input?.IsFocused == true)
        {
            flags |= 4;
        }

        var node = new JsonObject
        {
            ["id"] = session.GetNodeId(element),
            ["typeId"] = state.TypeId(element.GetType().FullName ?? element.GetType().Name),
            ["name"] = element.Name,
            ["automationId"] = AutomationProperties.GetAutomationId(element),
            ["label"] = GetLabel(element),
            ["childCount"] = children.Count,
            ["visual"] = AvaloniaAppearance.Capture(element)
        };

        if (element is Control control)
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(control);
            AvaloniaAccessibility.Populate(node, peer);
            node["label"] ??= GetLabel(element);
        }

        if (visual is not null)
        {
            var topLevel = TopLevel.GetTopLevel(visual);
            var localBounds = visual.Bounds;
            var transform = topLevel is null ? null : visual.TransformToVisual(topLevel);
            var absoluteBounds = transform.HasValue
                ? new Rect(localBounds.Size).TransformToAABB(transform.Value)
                : localBounds;

            node["bounds"] = new JsonArray(
                localBounds.X, localBounds.Y, localBounds.Width, localBounds.Height,
                absoluteBounds.X, absoluteBounds.Y, absoluteBounds.Width, absoluteBounds.Height);
            node["rootId"] = topLevel is null ? null : session.GetNodeId(topLevel);
            node["coordinateSpace"] = topLevel is null ? "detached-local" : "root-logical-pixels";
            node["renderScaling"] = topLevel?.RenderScaling;
            node["z"] = visual.ZIndex;
        }

        var childNodes = new JsonArray();

        foreach (var child in children)
        {
            if (remainingDepth == 0 || state.NodeCount >= state.MaximumNodes)
            {
                flags |= 32;
                state.Truncated = true;
                break;
            }

            childNodes.Add((JsonNode)BuildNode(child, remainingDepth - 1, state));
        }

        node["flags"] = flags;
        node["children"] = childNodes;

        return node;
    }

    internal static string? GetLabel(StyledElement element)
    {
        var label = element switch
        {
            TextBox textBox => textBox.PasswordChar == default ? textBox.PlaceholderText : "[redacted]",
            TextBlock textBlock => textBlock.Text,
            ContentControl { Content: string content } => content,
            _ => AutomationProperties.GetName(element)
        };

        return AvaloniaValueFormatter.Label(label);
    }

    internal static string PlatformName => OperatingSystem.IsAndroid() ? "android"
        : OperatingSystem.IsIOS() ? "ios"
        : OperatingSystem.IsMacOS() ? "macos"
        : OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsLinux() ? "linux"
        : "dotnet";

    private sealed class TreeBuildState
    {
        private readonly Dictionary<string, int> typeIds = new(StringComparer.Ordinal);

        internal TreeBuildState(int maximumNodes)
        {
            MaximumNodes = maximumNodes;
        }

        internal int MaximumNodes { get; }
        internal int NodeCount { get; set; }
        internal bool Truncated { get; set; }
        internal JsonArray Types { get; } = new JsonArray();

        internal int TypeId(string typeName)
        {
            if (!typeIds.TryGetValue(typeName, out var id))
            {
                id = Types.Count;
                Types.Add((JsonNode?)JsonValue.Create(typeName));
                typeIds.Add(typeName, id);
            }

            return id;
        }
    }
}
