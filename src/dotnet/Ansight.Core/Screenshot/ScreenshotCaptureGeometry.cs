using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ansight.Screenshot;

/// <summary>Capture-time layout in normalized coordinates of the final upright image.</summary>
public sealed record ScreenshotCaptureGeometry(
    string CaptureTarget,
    ScreenshotInsets? SafeAreaInsets,
    IReadOnlyDictionary<string, ScreenshotSystemUiRegion> SystemUi)
{
    public string CoordinateSpace => "image-normalized";

    private static readonly JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public JsonObject ToJson() => JsonSerializer.SerializeToNode(this, jsonOptions)!.AsObject();

    internal static ScreenshotCaptureGeometry AppWindow(ScreenshotInsets? insets, ScreenshotSystemUiRegion cutout)
        => new("appWindow", insets?.IsValid() == true ? insets : null,
            new Dictionary<string, ScreenshotSystemUiRegion>
            {
                ["statusBar"] = ScreenshotSystemUiRegion.Absent("App-window capture excludes the OS status-bar layer."),
                ["navigationBar"] = ScreenshotSystemUiRegion.Absent("App-window capture excludes the OS navigation-bar layer."),
                ["displayCutout"] = cutout
            });

#if ANDROID
    internal static ScreenshotCaptureGeometry Capture(Android.Views.View root)
    {
        var width = (double)root.Width;
        var height = (double)root.Height;
        var windowInsets = Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.M ? root.RootWindowInsets : null;
        ScreenshotInsets? safe = null;
        var cutout = ScreenshotSystemUiRegion.Unknown("Display cutout bounds are unavailable on this capture.");
        if (width > 0 && height > 0 && windowInsets is not null)
        {
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
            {
                var value = windowInsets.GetInsets(Android.Views.WindowInsets.Type.SystemBars() | Android.Views.WindowInsets.Type.DisplayCutout());
                safe = new(value.Top / height, value.Right / width, value.Bottom / height, value.Left / width);
            }
            else
            {
#pragma warning disable CA1422
                safe = new(windowInsets.StableInsetTop / height, windowInsets.StableInsetRight / width,
                    windowInsets.StableInsetBottom / height, windowInsets.StableInsetLeft / width);
#pragma warning restore CA1422
            }
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.P)
            {
                var regions = new List<ScreenshotRect>();
                foreach (var rect in windowInsets.DisplayCutout?.BoundingRects ?? Array.Empty<Android.Graphics.Rect>())
                {
                    var left = Math.Clamp(rect.Left, 0, root.Width);
                    var top = Math.Clamp(rect.Top, 0, root.Height);
                    var right = Math.Clamp(rect.Right, 0, root.Width);
                    var bottom = Math.Clamp(rect.Bottom, 0, root.Height);
                    if (right > left && bottom > top)
                        regions.Add(new(left / width, top / height, (right - left) / width, (bottom - top) / height));
                }
                cutout = regions.Count == 0 ? ScreenshotSystemUiRegion.Absent("No cutout intersects this window.")
                    : new("present", regions);
            }
        }
        return AppWindow(safe, cutout);
    }
#elif IOS || MACCATALYST
    internal static ScreenshotCaptureGeometry Capture(UIKit.UIWindow window)
    {
        var size = window.Bounds.Size;
        var insets = window.SafeAreaInsets;
        var safe = size.Width > 0 && size.Height > 0
            ? new ScreenshotInsets((double)(insets.Top / size.Height), (double)(insets.Right / size.Width),
                (double)(insets.Bottom / size.Height), (double)(insets.Left / size.Width)) : null;
        return AppWindow(safe, ScreenshotSystemUiRegion.Unknown("Public APIs do not provide exact cutout bounds for this image."));
    }
#endif
}

public sealed record ScreenshotInsets(double Top, double Right, double Bottom, double Left)
{
    internal bool IsValid() => new[] { Top, Right, Bottom, Left }.All(value => double.IsFinite(value) && value >= 0 && value <= 1)
        && Top + Bottom < 1 && Left + Right < 1;
}
public sealed record ScreenshotRect(double X, double Y, double Width, double Height);
public sealed record ScreenshotSystemUiRegion(string State, IReadOnlyList<ScreenshotRect> Regions, string? Reason = null)
{
    public static ScreenshotSystemUiRegion Absent(string reason) => new("absent", Array.Empty<ScreenshotRect>(), reason);
    public static ScreenshotSystemUiRegion Unknown(string reason) => new("unknown", Array.Empty<ScreenshotRect>(), reason);
}
