using System.Text.Json.Nodes;
using Avalonia.Controls;

namespace Ansight.Tools.Avalonia;

internal static class AvaloniaCoordinateSpace
{
    internal static JsonObject? Capture(TopLevel? topLevel, AvaloniaInspectionSession session)
    {
        if (topLevel is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["x"] = 0d,
            ["y"] = 0d,
            ["width"] = topLevel.ClientSize.Width,
            ["height"] = topLevel.ClientSize.Height,
            ["rootId"] = session.GetNodeId(topLevel),
            ["coordinateSystem"] = "root-logical-pixels",
            ["renderScaling"] = topLevel.RenderScaling
        };
    }
}
