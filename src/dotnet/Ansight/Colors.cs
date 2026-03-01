using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Ansight;

/// <summary>
/// Helpers for converting Ansight's internal color struct to MAUI colors.
/// </summary>
public static class Colors
{
    public static MauiColor BrandColor => ToMauiColor(Constants.BrandColor);
    public static MauiColor BrandColorFaded => ToMauiColor(Constants.BrandColor_Faded);

    public static MauiColor ToMauiColor(Color color) =>
        new MauiColor(color.RedNormalized, color.GreenNormalized, color.BlueNormalized, color.AlphaNormalized);
}
