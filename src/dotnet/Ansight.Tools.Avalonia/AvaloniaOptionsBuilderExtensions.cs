using System.Text.Json.Nodes;
using Ansight.Tools.VisualTree;

namespace Ansight.Tools.Avalonia;

public static class AvaloniaOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithAvaloniaTools(
        this Options.OptionsBuilder builder,
        AvaloniaInspectionSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        session ??= AvaloniaInspectionSession.Default;
        VisualTreeProviderRegistry.Register(new AvaloniaTreeProvider(session, AvaloniaTreeKind.Logical));
        VisualTreeProviderRegistry.Register(new AvaloniaTreeProvider(session, AvaloniaTreeKind.Visual));
        VisualTreeProviderRegistry.Register(new AvaloniaAccessibilityTreeProvider(session));

        if (!builder.ContainsTool(VisualTreeToolIds.GetVisualTree))
        {
            builder.WithVisualTreeTools();
        }

        foreach (var tool in CreateTools(session))
        {
            if (!builder.ContainsTool(tool.Id))
            {
                builder.AddTools([tool]);
            }
        }

        return builder;
    }

    public static IReadOnlyList<ITool> CreateTools(AvaloniaInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var inspector = new AvaloniaElementInspector(session);
        var rendering = new AvaloniaRenderingDiagnostics(session);
        var treeArguments = ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
        {
            ["rootNodeId"] = ToolSchema.String("Optional subtree root."),
            ["maxDepth"] = ToolSchema.Integer("Requested maximum depth, 0 to 64. Default 24; transport limits cap each capture at 24. Use rootNodeId to inspect deeper subtrees."),
            ["maxNodes"] = ToolSchema.Integer("Maximum nodes, 1 to 100000. Default 2000.")
        });
        var propertyArguments = new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Live node id from an Avalonia tree."),
            ["property"] = ToolSchema.String("Avalonia property name or owner-qualified name.")
        };
        var setPropertyArguments = new Dictionary<string, ToolSchema>(propertyArguments)
        {
            ["valueJson"] = ToolSchema.String("JSON scalar value. SetCurrentValue preserves an existing binding.")
        };

        return
        [
            new AvaloniaTool(AvaloniaToolIds.GetLogicalTree, "Get Avalonia Logical Tree",
                "Captures the logical hierarchy with stable ids, bounds and snapshot references.", ToolPolicy.Read,
                treeArguments, arguments => CaptureTree(arguments, "avalonia-logical")),
            new AvaloniaTool(AvaloniaToolIds.GetVisualTree, "Get Avalonia Visual Tree",
                "Captures the rendered control hierarchy including template children, with snapshot references.", ToolPolicy.Read,
                treeArguments, arguments => CaptureTree(arguments, "avalonia-visual")),
            new AvaloniaTool(AvaloniaToolIds.GetAccessibilityTree, "Get Avalonia Accessibility Tree",
                "Captures the framework automation-peer hierarchy, names, roles and states. Device accessibility is a separate source.", ToolPolicy.Read,
                treeArguments, arguments => CaptureTree(arguments, "avalonia-accessibility")),
            UiTool(AvaloniaToolIds.GetRoots, "Get Avalonia Roots", "Lists attached Avalonia top levels, including tracked popup roots.",
                ToolPolicy.Read, ToolSchema.Object(), _ => GetRoots(session)),
            UiTool(AvaloniaToolIds.InspectElement, "Inspect Avalonia Element",
                "Reads layout validity, classes, property value priorities and data context type. Input text is redacted.",
                ToolPolicy.Read, ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
                {
                    ["nodeId"] = ToolSchema.String("Live node id."),
                    ["maxProperties"] = ToolSchema.Integer("Property result limit, 1 to 256. Default 64.")
                }, required: ["nodeId"]), inspector.Inspect),
            UiTool(AvaloniaToolIds.GetProperty, "Get Avalonia Property", "Reads an Avalonia property and its effective value priority.",
                ToolPolicy.Read, ToolSchema.Object(properties: propertyArguments, required: ["nodeId", "property"]), inspector.GetProperty),
            UiTool(AvaloniaToolIds.SetProperty, "Set Avalonia Property", "Changes a scalar property with SetCurrentValue, preserving bindings.",
                ToolPolicy.Write, ToolSchema.Object(properties: setPropertyArguments, required: ["nodeId", "property", "valueJson"]),
                arguments => inspector.SetProperty(arguments, false)),
            UiTool(AvaloniaToolIds.ClearProperty, "Clear Avalonia Property", "Clears the local value or binding of an Avalonia property.",
                ToolPolicy.Write, ToolSchema.Object(properties: propertyArguments, required: ["nodeId", "property"]),
                arguments => inspector.SetProperty(arguments, true)),
            UiTool(AvaloniaToolIds.GetRenderingDiagnostics, "Get Avalonia Rendering Diagnostics",
                "Reads render scaling, overlay settings, visual counts and invalid layout counts for each top level.",
                ToolPolicy.Read, ToolSchema.Object(), rendering.Capture),
            new AvaloniaTool(AvaloniaToolIds.SampleRendering, "Sample Avalonia Rendering",
                "Samples animation callback cadence and layout updates. This does not measure GPU presentation FPS or GPU time.",
                ToolPolicy.Read, ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
                {
                    ["rootId"] = ToolSchema.String("Required when more than one root is attached."),
                    ["durationMilliseconds"] = ToolSchema.Integer("Sampling duration, 100 to 5000. Default 1000.")
                }), rendering.SampleAsync),
            UiTool(AvaloniaToolIds.SetRendererOverlays, "Set Avalonia Renderer Overlays",
                "Changes built-in render overlays so they can be captured in device screenshots. Use None to disable.",
                ToolPolicy.Write, ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
                {
                    ["rootId"] = ToolSchema.String("Required when more than one root is attached."),
                    ["overlays"] = ToolSchema.String("Comma-separated None, Fps, DirtyRects, LayoutTimeGraph, RenderTimeGraph.")
                }, required: ["overlays"]), rendering.SetOverlays)
        ];
    }

    private static Task<ToolResult> CaptureTree(IReadOnlyDictionary<string, string> arguments, string source)
    {
        var captureArguments = new Dictionary<string, string>(arguments) { ["source"] = source };

        return new GetVisualTreeTool().Execute(captureArguments);
    }

    private static ToolResult GetRoots(AvaloniaInspectionSession session)
    {
        var roots = new JsonArray();

        foreach (var root in session.GetRoots())
        {
            roots.Add((JsonNode)new JsonObject
            {
                ["nodeId"] = session.GetNodeId(root),
                ["type"] = root.GetType().FullName,
                ["name"] = root.Name,
                ["renderScaling"] = root.RenderScaling,
                ["clientSize"] = AvaloniaValueFormatter.Value(root.ClientSize)
            });
        }

        return ToolResult.Success(new JsonObject { ["roots"] = roots });
    }

    private static AvaloniaTool UiTool(string id, string name, string description, ToolPolicy policy,
        ToolSchema arguments, Func<IReadOnlyDictionary<string, string>, ToolResult> execute)
    {
        return new AvaloniaTool(id, name, description, policy, arguments,
            values => AvaloniaInspectionSession.OnUiThreadAsync(() => execute(values)));
    }

    private sealed class AvaloniaTool : ITool
    {
        private readonly Func<IReadOnlyDictionary<string, string>, Task<ToolResult>> execute;

        internal AvaloniaTool(string id, string name, string description, ToolPolicy policy,
            ToolSchema argumentsSchema, Func<IReadOnlyDictionary<string, string>, Task<ToolResult>> execute)
        {
            Id = id;
            Name = name;
            Description = description;
            Policy = policy;
            ArgumentsSchema = argumentsSchema;
            this.execute = execute;
        }

        public string Category => "avalonia";
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public ToolPolicy Policy { get; }
        public string Keywords => "avalonia xaml controls logical visual layout rendering properties";
        public ToolSchema ArgumentsSchema { get; }
        public ToolSchema ResultSchema => ToolSchema.Object(additionalProperties: true);

        public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);

            return execute(arguments);
        }
    }
}
