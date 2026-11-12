using System.ComponentModel;
using System.Text.Json.Nodes;
using Ansight.Tools;
using Ansight.Tools.Avalonia;
using Ansight.Tools.VisualTree;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using Xunit;

namespace Ansight.Avalonia.Tests;

public sealed class AvaloniaInspectionTests
{
    [AvaloniaFact]
    public async Task FrameworkTreesHonorAutomationNamesAndControlStates()
    {
        var label = new TextBlock { Text = "Accessible input label" };
        var text = new TextBox { Name = "input", Text = "private typed value" };
        var button = new Button { Name = "button", Content = "Visible caption" };
        var toggle = new CheckBox { Name = "toggle", IsChecked = true };
        var range = new Slider { Name = "range", Minimum = 2, Maximum = 8, Value = 5 };
        var selected = new ListBoxItem { Name = "selected", IsSelected = true };
        AutomationProperties.SetName(button, "Accessible action");
        AutomationProperties.SetLabeledBy(text, label);
        using var fixture = new InspectionFixture(new StackPanel { Children = { label, text, button, toggle, range, selected } });

        foreach (var kind in new[] { AvaloniaTreeKind.Logical, AvaloniaTreeKind.Visual })
        {
            var tree = await fixture.Capture(kind);
            var expectedNames = new[] { "input", "button", "toggle", "range", "selected" };
            var nodes = Nodes(tree)
                .Where(node => expectedNames.Contains(node["name"]?.GetValue<string>()))
                .ToDictionary(node => node["name"]!.GetValue<string>());

            Assert.Equal("Accessible action", nodes["button"]["label"]!.GetValue<string>());
            Assert.Equal("Accessible input label", nodes["input"]["label"]!.GetValue<string>());
            Assert.Equal("[redacted]", nodes["input"]["accessibility"]!["value"]!.GetValue<string>());
            Assert.True(nodes["toggle"]["checked"]!.GetValue<bool>());
            Assert.True(nodes["selected"]["selected"]!.GetValue<bool>());
            Assert.Equal(5, nodes["range"]["accessibility"]!["value"]!.GetValue<double>());
            Assert.Equal(2, nodes["range"]["accessibility"]!["minimum"]!.GetValue<double>());
            Assert.Equal(8, nodes["range"]["accessibility"]!["maximum"]!.GetValue<double>());
            Assert.DoesNotContain("private typed value", tree.ToJsonString());
        }
    }

    [AvaloniaFact]
    public async Task AccessibilityCaptureUsesPeerChildrenAndRejectsDetachedReferences()
    {
        var content = new PeerHierarchyControl();
        using var fixture = new InspectionFixture(content);
        var provider = new AvaloniaAccessibilityTreeProvider(fixture.Session);
        var captured = await provider.GetVisualTreeAsync(new Dictionary<string, string>());

        Assert.True(captured.IsSuccess, captured.Message);
        Assert.Equal("avalonia-accessibility", captured.Payload!["source"]!.GetValue<string>());
        var peerNode = Nodes(captured.Payload.AsObject()).Single(node => node["label"]?.GetValue<string>() == "Peer-only child");
        var nodeId = peerNode["id"]!.GetValue<string>();
        var visual = await fixture.Capture(AvaloniaTreeKind.Visual);

        Assert.DoesNotContain(Nodes(visual), node => node["label"]?.GetValue<string>() == "Peer-only child");
        var inspected = await provider.InspectNodeAsync(new Dictionary<string, string> { ["nodeId"] = nodeId });

        Assert.True(inspected.IsSuccess, inspected.Message);
        Assert.Equal(nodeId, inspected.Payload!["node"]!["id"]!.GetValue<string>());

        var subtree = await provider.GetVisualTreeAsync(new Dictionary<string, string> { ["rootNodeId"] = nodeId });

        Assert.True(subtree.IsSuccess, subtree.Message);
        AssertCoordinateExtent(subtree.Payload!, fixture);

        fixture.Window.Content = null;
        var detached = await provider.InspectNodeAsync(new Dictionary<string, string> { ["nodeId"] = nodeId });

        Assert.False(detached.IsSuccess);
    }

    [AvaloniaFact]
    public async Task AccessibilityCapturePreservesPasswordRedactionAndNodeBudgets()
    {
        var password = new TextBox { Text = "hidden-input-value", PasswordChar = '*', RevealPassword = true };
        using var fixture = new InspectionFixture(password);
        var provider = new AvaloniaAccessibilityTreeProvider(fixture.Session);
        var tree = await provider.GetVisualTreeAsync(new Dictionary<string, string>());

        Assert.True(tree.IsSuccess, tree.Message);
        Assert.DoesNotContain("hidden-input-value", tree.Payload!.ToJsonString());
        Assert.Contains("[redacted]", tree.Payload.ToJsonString());

        var limited = await provider.GetVisualTreeAsync(new Dictionary<string, string> { ["maxNodes"] = "1" });

        Assert.True(limited.IsSuccess, limited.Message);
        Assert.Equal(1, limited.Payload!["nodeCount"]!.GetValue<int>());
        Assert.True(limited.Payload["truncated"]!.GetValue<bool>());
    }

    [AvaloniaFact]
    public async Task TreeAppearancePreservesEffectiveBrushColorsAndAlpha()
    {
        var text = new TextBlock { Name = "caption", Text = "Styled", Foreground = Brushes.White };
        var border = new Border
        {
            Name = "surface",
            Background = new SolidColorBrush(Color.FromArgb(128, 10, 20, 30), 0.5),
            BorderBrush = Brushes.Red,
            Opacity = 0.75,
            Child = text
        };
        using var fixture = new InspectionFixture(border);
        var first = await fixture.Capture(AvaloniaTreeKind.Visual);
        var surface = Nodes(first).Single(node => node["name"]?.GetValue<string>() == "surface");
        var caption = Nodes(first).Single(node => node["name"]?.GetValue<string>() == "caption");

        Assert.Equal("#400A141E", surface["visual"]!["background"]!.GetValue<string>());
        Assert.Equal("#FFFF0000", surface["visual"]!["borderColor"]!.GetValue<string>());
        Assert.Equal(0.75, surface["visual"]!["opacity"]!.GetValue<double>());
        Assert.Equal("#FFFFFFFF", caption["visual"]!["foreground"]!.GetValue<string>());

        border.Background = Brushes.Blue;
        var changed = await fixture.Capture(AvaloniaTreeKind.Visual);
        var changedSurface = Nodes(changed).Single(node => node["name"]?.GetValue<string>() == "surface");

        Assert.Equal("#FF0000FF", changedSurface["visual"]!["background"]!.GetValue<string>());
    }

    [AvaloniaFact]
    public async Task LogicalAndVisualTreesShareIdentityButExposeDifferentChildren()
    {
        var templateChild = new TextBlock { Name = "templateChild", Text = "Template only" };
        var button = new Button
        {
            Name = "actionButton",
            Content = "Press",
            Width = 100,
            Height = 30,
            Template = new FuncControlTemplate<Button>((_, _) => new Border { Child = templateChild })
        };
        AutomationProperties.SetAutomationId(button, "action-button");
        using var fixture = new InspectionFixture(button);
        var logical = await fixture.Capture(AvaloniaTreeKind.Logical);
        var visual = await fixture.Capture(AvaloniaTreeKind.Visual);
        var logicalButton = Nodes(logical).Single(node => node["name"]?.GetValue<string>() == "actionButton");
        var visualButton = Nodes(visual).Single(node => node["name"]?.GetValue<string>() == "actionButton");

        Assert.Equal(logicalButton["id"]!.GetValue<string>(), visualButton["id"]!.GetValue<string>());
        Assert.Equal("action-button", visualButton["automationId"]!.GetValue<string>());
        Assert.DoesNotContain(Nodes(logical), node => node["name"]?.GetValue<string>() == "templateChild");
        Assert.Contains(Nodes(visual), node => node["name"]?.GetValue<string>() == "templateChild");
        Assert.Equal(8, visualButton["bounds"]!.AsArray().Count);
        Assert.Equal(1, visualButton["renderScaling"]!.GetValue<double>());
        Assert.Equal("button", visualButton["role"]!.GetValue<string>());
        Assert.Contains("tap", visualButton["supportedActions"]!.AsArray().Select(action => action!.GetValue<string>()));
    }

    [AvaloniaFact]
    public async Task FrameworkSubtreesKeepTheTopLevelCoordinateExtent()
    {
        var button = new Button { Width = 80, Height = 30, Content = "Offset target" };
        Canvas.SetLeft(button, 90);
        Canvas.SetTop(button, 70);
        using var fixture = new InspectionFixture(new Canvas { Children = { button } });

        foreach (var kind in new[] { AvaloniaTreeKind.Logical, AvaloniaTreeKind.Visual })
        {
            var captured = await fixture.Provider(kind).GetVisualTreeAsync(new Dictionary<string, string>
            {
                ["rootNodeId"] = fixture.Session.GetNodeId(button),
                ["maxNodes"] = "1"
            });

            Assert.True(captured.IsSuccess, captured.Message);
            var payload = captured.Payload!;
            Assert.Equal(90, payload["root"]!["bounds"]![4]!.GetValue<double>());
            Assert.Equal(70, payload["root"]!["bounds"]![5]!.GetValue<double>());
            Assert.Equal(80, payload["root"]!["bounds"]![6]!.GetValue<double>());
            AssertCoordinateExtent(payload, fixture);
        }
    }

    [AvaloniaFact]
    public async Task AccessibilitySubtreesKeepTheTopLevelCoordinateExtentAfterResize()
    {
        var button = new Button { Width = 80, Height = 30, Content = "Accessible target" };
        Canvas.SetLeft(button, 90);
        Canvas.SetTop(button, 70);
        using var fixture = new InspectionFixture(new Canvas { Children = { button } });
        var provider = new AvaloniaAccessibilityTreeProvider(fixture.Session);
        var arguments = new Dictionary<string, string>
        {
            ["rootNodeId"] = fixture.Session.GetNodeId(button),
            ["maxNodes"] = "1"
        };
        var captured = await provider.GetVisualTreeAsync(arguments);

        Assert.True(captured.IsSuccess, captured.Message);
        Assert.Equal(90, captured.Payload!["root"]!["bounds"]!["x"]!.GetValue<double>());
        Assert.Equal(70, captured.Payload["root"]!["bounds"]!["y"]!.GetValue<double>());
        AssertCoordinateExtent(captured.Payload, fixture);

        fixture.Window.Width = 600;
        fixture.Window.Height = 450;
        fixture.Window.UpdateLayout();
        var resized = await provider.GetVisualTreeAsync(arguments);

        Assert.True(resized.IsSuccess, resized.Message);
        AssertCoordinateExtent(resized.Payload!, fixture);
        Assert.Equal(600, resized.Payload!["coordinateSpace"]!["width"]!.GetValue<double>());
        Assert.Equal(450, resized.Payload["coordinateSpace"]!["height"]!.GetValue<double>());
    }

    private static void AssertCoordinateExtent(JsonNode payload, InspectionFixture fixture)
    {
        var space = payload["coordinateSpace"]!;
        Assert.Equal(0, space["x"]!.GetValue<double>());
        Assert.Equal(0, space["y"]!.GetValue<double>());
        Assert.Equal(fixture.Window.ClientSize.Width, space["width"]!.GetValue<double>());
        Assert.Equal(fixture.Window.ClientSize.Height, space["height"]!.GetValue<double>());
        Assert.Equal(fixture.Session.GetNodeId(fixture.Window), space["rootId"]!.GetValue<string>());
        Assert.Equal(fixture.Window.RenderScaling, space["renderScaling"]!.GetValue<double>());
    }

    [AvaloniaFact]
    public async Task DeepAndMultipleRootTreesStayWithinTransportAndNodeBudgets()
    {
        Control content = new TextBlock { Text = "Deep leaf" };

        for (var depth = 0; depth < 60; depth++)
        {
            content = new Border { Child = content };
        }

        using var fixture = new InspectionFixture(content);
        var deep = await fixture.Provider(AvaloniaTreeKind.Visual).GetVisualTreeAsync(
            new Dictionary<string, string> { ["maxDepth"] = "64" });

        Assert.True(deep.IsSuccess, deep.Message);
        Assert.True(deep.Payload!["truncated"]!.GetValue<bool>());
        var envelope = new JsonObject { ["envelope"] = new JsonObject { ["payload"] = deep.Payload.DeepClone() } };
        Assert.NotNull(JsonNode.Parse(envelope.ToJsonString()));

        var secondWindow = new Window { Content = new Button(), Width = 100, Height = 100 };

        try
        {
            secondWindow.Show();
            fixture.Session.RegisterRoot(secondWindow);
            var limited = await fixture.Provider(AvaloniaTreeKind.Visual).GetVisualTreeAsync(
                new Dictionary<string, string> { ["maxNodes"] = "1" });

            Assert.True(limited.IsSuccess, limited.Message);
            Assert.True(limited.Payload!["truncated"]!.GetValue<bool>());
            Assert.Equal(1, limited.Payload["nodeCount"]!.GetValue<int>());
            Assert.Single(Nodes(limited.Payload.AsObject()));
            Assert.Equal("avalonia:application", limited.Payload["root"]!["id"]!.GetValue<string>());
            Assert.Null(limited.Payload["coordinateSpace"]);
        }
        finally
        {
            secondWindow.Close();
        }
    }

    [AvaloniaFact]
    public async Task GenericNumericStringValueReachesRangeControl()
    {
        var slider = new Slider { Minimum = 0, Maximum = 10, Value = 1 };
        using var fixture = new InspectionFixture(slider);
        var provider = fixture.Provider(AvaloniaTreeKind.Visual);
        var request = new VisualTreeActionRequest(fixture.Session.GetNodeId(slider), "setValue", JsonValue.Create("7.5"), new JsonObject());
        var result = await provider.PerformActionAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(7.5, slider.Value);

        var rejected = await provider.PerformActionAsync(request with { Value = JsonValue.Create("NaN") }, CancellationToken.None);

        Assert.False(rejected.IsSuccess);
        Assert.Equal(7.5, slider.Value);
    }

    [AvaloniaFact]
    public async Task TruncationAndDetachedNodesAreExplicit()
    {
        var button = new Button { Content = "Press" };
        using var fixture = new InspectionFixture(button);
        var provider = fixture.Provider(AvaloniaTreeKind.Visual);
        var limited = await provider.GetVisualTreeAsync(new Dictionary<string, string> { ["maxNodes"] = "1" });

        Assert.True(limited.IsSuccess, limited.Message);
        Assert.True(limited.Payload!["truncated"]!.GetValue<bool>());
        Assert.Equal(1, limited.Payload["nodeCount"]!.GetValue<int>());

        var nodeId = fixture.Session.GetNodeId(button);
        fixture.Window.Content = null;
        var detached = await provider.InspectNodeAsync(new Dictionary<string, string> { ["nodeId"] = nodeId });

        Assert.False(detached.IsSuccess);
        Assert.Equal("avalonia_node_not_found", detached.ErrorCode);
    }

    [AvaloniaFact]
    public async Task GenericSnapshotQueryAndActionInvokeTheActualControlAndRejectStaleNodes()
    {
        var clicks = 0;
        var button = new Button { Content = "Run" };
        AutomationProperties.SetAutomationId(button, "run");
        button.Click += (_, _) => clicks++;
        using var fixture = new InspectionFixture(button);
        Options.CreateBuilder().WithAvaloniaTools(fixture.Session);
        var query = await new QueryNodesTool().ExecuteAsync(Invocation(new JsonObject
        {
            ["source"] = "avalonia-visual",
            ["automationId"] = "run"
        }), CancellationToken.None);

        Assert.True(query.IsSuccess, query.Message);
        var matches = query.Payload!["matches"]!.AsArray();
        var match = Assert.Single(matches)!;
        var actionArguments = new JsonObject { ["reference"] = match["reference"]!.DeepClone(), ["action"] = "tap" };
        var action = await new PerformActionTool().ExecuteAsync(Invocation(actionArguments), CancellationToken.None);

        Assert.True(action.IsSuccess, action.Message);
        Assert.Equal(1, clicks);

        fixture.Window.Content = null;
        var stale = await new PerformActionTool().ExecuteAsync(Invocation(actionArguments), CancellationToken.None);

        Assert.False(stale.IsSuccess);
        Assert.Equal("stale_node_reference", stale.ErrorCode);
        Assert.Equal(1, clicks);
    }

    [AvaloniaFact]
    public async Task ActionsRespectDisabledAndReadOnlyControls()
    {
        var textBox = new TextBox { Text = "initial", IsReadOnly = true };
        using var fixture = new InspectionFixture(textBox);
        var provider = fixture.Provider(AvaloniaTreeKind.Visual);
        var request = new VisualTreeActionRequest(fixture.Session.GetNodeId(textBox), "setValue", JsonValue.Create("changed"), new JsonObject());
        var readOnly = await provider.PerformActionAsync(request, CancellationToken.None);

        Assert.False(readOnly.IsSuccess);
        Assert.Equal("avalonia_element_read_only", readOnly.ErrorCode);
        Assert.Equal("initial", textBox.Text);

        textBox.IsReadOnly = false;
        textBox.IsEnabled = false;
        var disabled = await provider.PerformActionAsync(request, CancellationToken.None);

        Assert.False(disabled.IsSuccess);
        Assert.Equal("avalonia_element_disabled", disabled.ErrorCode);

        textBox.IsEnabled = true;
        var enabled = await provider.PerformActionAsync(request, CancellationToken.None);

        Assert.True(enabled.IsSuccess, enabled.Message);
        Assert.Equal("changed", textBox.Text);
    }

    [AvaloniaFact]
    public async Task PasswordValuesAreRedactedInTreeAndPropertyInspection()
    {
        var textBox = new TextBox { Text = "sensitive-input-value", PasswordChar = '*', RevealPassword = true };
        using var fixture = new InspectionFixture(textBox);
        var tree = await fixture.Capture(AvaloniaTreeKind.Visual);
        var properties = await fixture.Tool(AvaloniaToolIds.InspectElement).Execute(new Dictionary<string, string>
        {
            ["nodeId"] = fixture.Session.GetNodeId(textBox),
            ["maxProperties"] = "256"
        });

        Assert.True(properties.IsSuccess, properties.Message);
        Assert.DoesNotContain("sensitive-input-value", tree.ToJsonString());
        Assert.DoesNotContain("sensitive-input-value", properties.Payload!.ToJsonString());
        Assert.Contains("[redacted]", properties.Payload.ToJsonString());
    }

    [AvaloniaFact]
    public async Task SettingCurrentPropertyValuePreservesTheBinding()
    {
        var model = new CaptionModel();
        var text = new TextBlock { DataContext = model };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(CaptionModel.Caption)));
        using var fixture = new InspectionFixture(text);
        var result = await fixture.Tool(AvaloniaToolIds.SetProperty).Execute(new Dictionary<string, string>
        {
            ["nodeId"] = fixture.Session.GetNodeId(text),
            ["property"] = "Text",
            ["valueJson"] = "\"temporary\""
        });

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("temporary", text.Text);

        model.Caption = "source changed";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("source changed", text.Text);
    }

    [AvaloniaFact]
    public async Task RendererOverlaysAndSamplingReportWhatIsActuallyMeasured()
    {
        using var fixture = new InspectionFixture(new Border());
        var set = await fixture.Tool(AvaloniaToolIds.SetRendererOverlays).Execute(new Dictionary<string, string>
        {
            ["overlays"] = "Fps,DirtyRects"
        });

        Assert.True(set.IsSuccess, set.Message);
        Assert.Equal(RendererDebugOverlays.Fps | RendererDebugOverlays.DirtyRects, fixture.Window.RendererDiagnostics.DebugOverlays);

        var diagnostics = await fixture.Tool(AvaloniaToolIds.GetRenderingDiagnostics).Execute(new Dictionary<string, string>());
        var sample = await fixture.Tool(AvaloniaToolIds.SampleRendering).Execute(new Dictionary<string, string> { ["durationMilliseconds"] = "100" });

        Assert.True(diagnostics.IsSuccess, diagnostics.Message);
        Assert.False(diagnostics.Payload!["gpuTimingAvailable"]!.GetValue<bool>());
        Assert.True(sample.IsSuccess, sample.Message);
        Assert.Equal("animation-callback-cadence", sample.Payload!["measurement"]!.GetValue<string>());
        Assert.False(sample.Payload["isGpuPresentationRate"]!.GetValue<bool>());
        Assert.True(sample.Payload["elapsedMilliseconds"]!.GetValue<double>() >= 100);
    }

    [AvaloniaFact]
    public void ClosedRootsAreRemovedAndTrackingDoesNotRequireDesktopLifetime()
    {
        using var session = new AvaloniaInspectionSession();
        session.StartTrackingRoots();
        var window = new Window { Content = new Border(), Width = 200, Height = 200 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(window, session.GetRoots());

        window.Close();

        Assert.DoesNotContain(window, session.GetRoots());
    }

    private static IEnumerable<JsonObject> Nodes(JsonObject payload)
    {
        var pending = new Stack<JsonObject>();
        pending.Push(payload["root"]!.AsObject());

        while (pending.TryPop(out var node))
        {
            yield return node;

            foreach (var child in node["children"]!.AsArray().OfType<JsonObject>())
            {
                pending.Push(child);
            }
        }
    }

    private static ToolInvocation Invocation(JsonObject arguments)
    {
        return new ToolInvocation(arguments, new ToolInvocationContext(Guid.NewGuid().ToString(), null, null));
    }

    private sealed class InspectionFixture : IDisposable
    {
        internal InspectionFixture(Control content)
        {
            Window = new Window { Content = content, Width = 400, Height = 300 };
            Window.Show();
            Window.UpdateLayout();
            Session.RegisterRoot(Window);
        }

        internal AvaloniaInspectionSession Session { get; } = new AvaloniaInspectionSession();
        internal Window Window { get; }

        internal AvaloniaTreeProvider Provider(AvaloniaTreeKind kind)
        {
            return new AvaloniaTreeProvider(Session, kind);
        }

        internal ITool Tool(string id)
        {
            return AvaloniaOptionsBuilderExtensions.CreateTools(Session).Single(tool => tool.Id == id);
        }

        internal async Task<JsonObject> Capture(AvaloniaTreeKind kind)
        {
            var result = await Provider(kind).GetVisualTreeAsync(new Dictionary<string, string>());
            Assert.True(result.IsSuccess, result.Message);

            return result.Payload!.AsObject();
        }

        public void Dispose()
        {
            Window.Close();
            Session.Dispose();
        }
    }

    private sealed class CaptionModel : INotifyPropertyChanged
    {
        private string caption = "original";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Caption
        {
            get => caption;
            set
            {
                caption = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));
            }
        }
    }

    private sealed class PeerHierarchyControl : Control
    {
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new HierarchyPeer(this);
        }
    }

    private sealed class HierarchyPeer : ControlAutomationPeer
    {
        private readonly AutomationPeer child;

        internal HierarchyPeer(Control owner) : base(owner)
        {
            var peerOnlyControl = new Button();
            AutomationProperties.SetName(peerOnlyControl, "Peer-only child");
            child = CreatePeerForElement(peerOnlyControl);
        }

        protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
        {
            return [child];
        }
    }
}
