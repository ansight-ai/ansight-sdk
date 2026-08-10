using System.Text.Json.Nodes;

namespace Ansight.Screenshot;

internal sealed record HostSessionJpegCapturePolicy(bool UseHostCapture, string? Source)
{
    public const string ControlVersionPropertyName = "sessionJpegCaptureControlVersion";
    public const int ControlVersion = 1;

    public static HostSessionJpegCapturePolicy App { get; } = new(false, null);

    public static void AddClientCapabilities(
        JsonObject payload,
        SessionJpegCaptureOptions? captureOptions)
    {
        ArgumentNullException.ThrowIfNull(payload);

        payload[ControlVersionPropertyName] = ControlVersion;
        if (captureOptions is null)
        {
            return;
        }

        payload["sessionJpegCapture"] = new JsonObject
        {
            ["maxWidth"] = captureOptions.MaxWidth
        };
    }

    public static HostSessionJpegCapturePolicy FromPayload(JsonObject? payload)
    {
        var capture = payload?["sessionJpegCapture"] as JsonObject;
        var mode = capture?["mode"]?.GetValue<string>();
        return string.Equals(mode, "host", StringComparison.OrdinalIgnoreCase)
            ? new HostSessionJpegCapturePolicy(
                true,
                capture?["source"]?.GetValue<string>())
            : App;
    }
}
