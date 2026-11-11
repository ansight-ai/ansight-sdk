using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Ansight.Tools.Avalonia;

internal static class AvaloniaAppearance
{
    internal static JsonObject Capture(StyledElement element)
    {
        var background = element switch
        {
            Border border => border.Background,
            Panel panel => panel.Background,
            TextBlock text => text.Background,
            TextPresenter text => text.Background,
            TemplatedControl control => control.Background,
            ContentPresenter presenter => presenter.Background,
            Shape shape => shape.Fill,
            _ => null
        };
        var foreground = element switch
        {
            TextBlock text => text.Foreground,
            TextPresenter text => text.Foreground,
            TemplatedControl control => control.Foreground,
            ContentPresenter presenter => presenter.Foreground,
            Shape shape => shape.Stroke,
            _ => null
        };
        var borderBrush = element switch
        {
            Border border => border.BorderBrush,
            TemplatedControl control => control.BorderBrush,
            ContentPresenter presenter => presenter.BorderBrush,
            _ => null
        };

        return new JsonObject
        {
            ["opacity"] = (element as Visual)?.Opacity ?? 1,
            ["text"] = AvaloniaTreeProvider.GetLabel(element),
            ["background"] = SolidColor(background),
            ["foreground"] = SolidColor(foreground),
            ["borderColor"] = SolidColor(borderBrush),
            ["backgroundBrush"] = DescribeBrush(background),
            ["foregroundBrush"] = DescribeBrush(foreground)
        };
    }

    private static string? SolidColor(IBrush? brush)
    {
        if (brush is not ISolidColorBrush solid)
        {
            return null;
        }

        var alpha = (byte)Math.Round(solid.Color.A * Math.Clamp(solid.Opacity, 0, 1));
        return $"#{alpha:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}";
    }

    private static JsonObject? DescribeBrush(IBrush? brush)
    {
        if (brush is null)
        {
            return null;
        }

        var result = new JsonObject
        {
            ["type"] = brush.GetType().Name,
            ["opacity"] = brush.Opacity,
            ["color"] = SolidColor(brush)
        };

        if (brush is IGradientBrush gradient)
        {
            var stops = new JsonArray();

            foreach (var stop in gradient.GradientStops.Take(64))
            {
                stops.Add((JsonNode)new JsonObject
                {
                    ["offset"] = stop.Offset,
                    ["color"] = $"#{stop.Color.A:X2}{stop.Color.R:X2}{stop.Color.G:X2}{stop.Color.B:X2}"
                });
            }

            result["gradientStops"] = stops;
            result["spreadMethod"] = gradient.SpreadMethod.ToString();
            result["stopsTruncated"] = gradient.GradientStops.Count > 64;
        }

        return result;
    }
}
