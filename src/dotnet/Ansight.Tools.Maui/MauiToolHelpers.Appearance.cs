namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

internal static partial class MauiToolHelpers
{
    private static void AddAppearanceProperties(Element element, JsonObject properties)
    {
        properties["backgroundColor"] = GetBackgroundColor(element)?.ToArgbHex();
        if (element is View view) properties["marginInsets"] = DescribeThickness(view.Margin);
        if (element is Microsoft.Maui.Controls.Layout layout) properties["paddingInsets"] = DescribeThickness(layout.Padding);
        if (element is Page page) properties["paddingInsets"] = DescribeThickness(page.Padding);
        if (element is StackBase stack) properties["spacing"] = stack.Spacing;
        if (element is StackLayout legacyStack) properties["spacing"] = legacyStack.Spacing;
        if (element is Grid grid)
        {
            properties["rowSpacing"] = grid.RowSpacing;
            properties["columnSpacing"] = grid.ColumnSpacing;
        }
        if (element is Border border)
        {
            properties["paddingInsets"] = DescribeThickness(border.Padding);
            properties["border"] = new JsonObject
            {
                ["color"] = (border.Stroke as SolidColorBrush)?.Color.ToArgbHex(),
                ["thickness"] = border.StrokeThickness,
                ["shape"] = border.StrokeShape?.GetType().Name
            };
        }
        switch (element)
        {
            case Label label:
                AddTextAppearance(properties, label.FontFamily, label.FontSize, label.FontAttributes, label.TextColor, label.CharacterSpacing);
                properties["horizontalTextAlignment"] = label.HorizontalTextAlignment.ToString();
                properties["verticalTextAlignment"] = label.VerticalTextAlignment.ToString();
                properties["lineBreakMode"] = label.LineBreakMode.ToString();
                properties["maxLines"] = label.MaxLines;
                properties["paddingInsets"] = DescribeThickness(label.Padding);
                break;
            case Button button:
                AddTextAppearance(properties, button.FontFamily, button.FontSize, button.FontAttributes, button.TextColor, button.CharacterSpacing);
                properties["paddingInsets"] = DescribeThickness(button.Padding);
                properties["border"] = new JsonObject { ["color"] = button.BorderColor?.ToArgbHex(), ["thickness"] = button.BorderWidth, ["cornerRadius"] = button.CornerRadius };
                break;
            case Entry entry:
                AddTextAppearance(properties, entry.FontFamily, entry.FontSize, entry.FontAttributes, entry.TextColor, entry.CharacterSpacing);
                properties["horizontalTextAlignment"] = entry.HorizontalTextAlignment.ToString();
                properties["verticalTextAlignment"] = entry.VerticalTextAlignment.ToString();
                break;
            case Editor editor:
                AddTextAppearance(properties, editor.FontFamily, editor.FontSize, editor.FontAttributes, editor.TextColor, editor.CharacterSpacing);
                properties["horizontalTextAlignment"] = editor.HorizontalTextAlignment.ToString();
                properties["verticalTextAlignment"] = editor.VerticalTextAlignment.ToString();
                break;
            case Picker picker:
                AddTextAppearance(properties, picker.FontFamily, picker.FontSize, picker.FontAttributes, picker.TextColor, picker.CharacterSpacing);
                break;
        }
    }

    private static JsonObject DescribeThickness(Microsoft.Maui.Thickness value)
        => new() { ["left"] = value.Left, ["top"] = value.Top, ["right"] = value.Right, ["bottom"] = value.Bottom };

    private static void AddTextAppearance(JsonObject properties, string? family, double size, FontAttributes attributes, Color? color, double spacing)
    {
        properties["font"] = new JsonObject { ["family"] = family, ["size"] = size, ["attributes"] = attributes.ToString(), ["characterSpacing"] = spacing };
        properties["textColor"] = color?.ToArgbHex();
    }
}
#endif
