using System.Globalization;
using Ansight.Screenshot;

namespace Ansight;

internal static class AnsightSessionConfigurationProperties
{
    internal const string CaptureGroup = "ansight.capture";
    internal const string TelemetryGroup = "ansight.telemetry";

    internal static void Apply(
        SessionCustomProperties properties,
        Options options,
        HostSessionJpegCapturePolicy capturePolicy)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(capturePolicy);

        properties.RemoveGroup(CaptureGroup);
        properties.RemoveGroup(TelemetryGroup);

        var captureOptions = options.SessionJpegCapture;
        properties
            .Register(CaptureGroup, "schemaVersion", "1")
            .Register(CaptureGroup, "enabled", FormatBoolean(captureOptions is not null || capturePolicy.UseHostCapture))
            .Register(
                CaptureGroup,
                "owner",
                capturePolicy.UseHostCapture ? "host" : captureOptions is null ? "none" : "app");

        if (captureOptions is not null)
        {
            properties
                .Register(CaptureGroup, "intervalMilliseconds", FormatNumber(captureOptions.IntervalMilliseconds))
                .Register(CaptureGroup, "quality", FormatNumber(captureOptions.Quality))
                .Register(CaptureGroup, "maxWidth", captureOptions.MaxWidth is { } maxWidth ? FormatNumber(maxWidth) : "full")
                .Register(CaptureGroup, "captureGpuBackedSurfaces", FormatBoolean(captureOptions.CaptureGpuBackedSurfaces))
                .Register(CaptureGroup, "captureKeyboardPresence", FormatBoolean(captureOptions.CaptureKeyboardPresence))
                .Register(CaptureGroup, "mode", FormatCaptureMode(captureOptions.Mode));
        }

        if (!string.IsNullOrWhiteSpace(capturePolicy.Source))
        {
            properties.Register(CaptureGroup, "ownerSource", capturePolicy.Source.Trim());
        }

        properties
            .Register(TelemetryGroup, "schemaVersion", "1")
            .Register(TelemetryGroup, "sampleFrequencyMilliseconds", FormatNumber(options.SampleFrequencyMilliseconds))
            .Register(TelemetryGroup, "retentionPeriodSeconds", FormatNumber(options.RetentionPeriodSeconds))
            .Register(TelemetryGroup, "framesPerSecond", FormatBoolean(options.EnableFramesPerSecond))
            .Register(TelemetryGroup, "batteryLevel", FormatBoolean(options.EnableBatteryLevel))
            .Register(TelemetryGroup, "openFileHandles", FormatBoolean(options.EnableOpenFileHandleTracking))
            .Register(TelemetryGroup, "jniReferenceCount", FormatBoolean(options.EnableJniReferenceCountTracking));
    }

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static string FormatNumber<T>(T value) where T : IFormattable
        => value.ToString(null, CultureInfo.InvariantCulture);

    private static string FormatCaptureMode(SessionJpegCaptureMode mode) => mode switch
    {
        SessionJpegCaptureMode.ScreenshotAndVisualTree => "screenshotAndVisualTree",
        SessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch => "screenshotWithVisualTreeOnTouch",
        _ => "screenshotOnly"
    };
}
