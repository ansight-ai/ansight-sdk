using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Layout;

namespace Ansight.Tools.Avalonia;

internal sealed class AvaloniaElementInspector
{
    private readonly AvaloniaInspectionSession session;

    internal AvaloniaElementInspector(AvaloniaInspectionSession session)
    {
        this.session = session;
    }

    internal ToolResult Inspect(IReadOnlyDictionary<string, string> arguments)
    {
        var element = session.ResolveNode(AvaloniaToolArguments.Required(arguments, "nodeId"));
        var maximum = AvaloniaToolArguments.Integer(arguments, "maxProperties", 64, 1, 256);
        var properties = Properties(element).ToArray();
        var values = new JsonArray();

        foreach (var property in properties.Take(maximum))
        {
            values.Add((JsonNode)DescribeProperty(element, property));
        }

        var result = new JsonObject
        {
            ["nodeId"] = session.GetNodeId(element),
            ["type"] = element.GetType().FullName,
            ["name"] = element.Name,
            ["label"] = AvaloniaTreeProvider.GetLabel(element),
            ["dataContextType"] = element.DataContext?.GetType().FullName,
            ["classes"] = new JsonArray(element.Classes.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["properties"] = values,
            ["propertyCount"] = properties.Length,
            ["truncated"] = properties.Length > maximum
        };

        if (element is Layoutable layout)
        {
            result["layout"] = new JsonObject
            {
                ["bounds"] = AvaloniaValueFormatter.Value(layout.Bounds),
                ["desiredSize"] = AvaloniaValueFormatter.Value(layout.DesiredSize),
                ["measureValid"] = layout.IsMeasureValid,
                ["arrangeValid"] = layout.IsArrangeValid,
                ["margin"] = AvaloniaValueFormatter.Value(layout.Margin),
                ["clipToBounds"] = layout.ClipToBounds,
                ["renderTransform"] = layout.RenderTransform?.Value.ToString()
            };
        }

        return ToolResult.Success(result);
    }

    internal ToolResult GetProperty(IReadOnlyDictionary<string, string> arguments)
    {
        var element = session.ResolveNode(AvaloniaToolArguments.Required(arguments, "nodeId"));
        var property = ResolveProperty(element, AvaloniaToolArguments.Required(arguments, "property"));

        return ToolResult.Success(DescribeProperty(element, property));
    }

    internal ToolResult SetProperty(IReadOnlyDictionary<string, string> arguments, bool clear)
    {
        var element = session.ResolveNode(AvaloniaToolArguments.Required(arguments, "nodeId"));
        var property = ResolveProperty(element, AvaloniaToolArguments.Required(arguments, "property"));

        if (property.IsReadOnly)
        {
            return ToolResult.Failure("The Avalonia property is read-only.", "avalonia_property_read_only");
        }

        if (clear)
        {
            element.ClearValue(property);
        }
        else
        {
            var value = AvaloniaValueFormatter.ParseScalar(AvaloniaToolArguments.Required(arguments, "valueJson"), property.PropertyType);
            element.SetCurrentValue(property, value);
        }

        return ToolResult.Success(DescribeProperty(element, property));
    }

    private static IEnumerable<AvaloniaProperty> Properties(StyledElement element)
    {
        var registry = AvaloniaPropertyRegistry.Instance;
        var type = element.GetType();

        return registry.GetRegistered(type)
            .Concat(registry.GetRegisteredDirect(type))
            .Concat(registry.GetRegisteredAttached(type))
            .Distinct()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ThenBy(property => property.OwnerType.FullName, StringComparer.Ordinal);
    }

    private static AvaloniaProperty ResolveProperty(StyledElement element, string name)
    {
        var matches = Properties(element)
            .Where(property => property.Name == name || $"{property.OwnerType.FullName}.{property.Name}" == name)
            .ToArray();

        if (matches.Length != 1)
        {
            throw new ArgumentException("The property name was not found or is ambiguous. Use the owner-qualified name from inspect_element.");
        }

        return matches[0];
    }

    private static JsonObject DescribeProperty(StyledElement element, AvaloniaProperty property)
    {
        var diagnostic = element.GetDiagnostic(property);

        return new JsonObject
        {
            ["name"] = property.Name,
            ["qualifiedName"] = $"{property.OwnerType.FullName}.{property.Name}",
            ["type"] = property.PropertyType.FullName,
            ["readOnly"] = property.IsReadOnly,
            ["isSet"] = element.IsSet(property),
            ["priority"] = diagnostic.Priority.ToString(),
            ["diagnostic"] = AvaloniaValueFormatter.Label(diagnostic.Diagnostic),
            ["currentValueOverride"] = diagnostic.IsOverriddenCurrentValue,
            ["value"] = AvaloniaValueFormatter.PropertyValue(element, property, diagnostic.Value)
        };
    }
}
