namespace Ansight.OfflineCapture;

internal static class OfflineCapturePaths
{
    public const string SettingsFileName = "settings.json";
    public const string SessionsDirectoryName = "sessions";
    public const string ManifestFileName = "manifest.json";

    public static string SettingsPath(string rootDirectory)
        => Path.Combine(rootDirectory, SettingsFileName);

    public static string SessionsDirectory(string rootDirectory)
        => Path.Combine(rootDirectory, SessionsDirectoryName);

    public static string SessionDirectory(string rootDirectory, string sessionId)
        => Path.Combine(SessionsDirectory(rootDirectory), sessionId);

    public static string ManifestPath(string sessionDirectory)
        => Path.Combine(sessionDirectory, ManifestFileName);

    public static string ChannelsPath(string sessionDirectory)
        => Path.Combine(sessionDirectory, "metadata", "channels.json");

    public static string DeviceProfilePath(string sessionDirectory)
        => Path.Combine(sessionDirectory, "metadata", "device-profile.json");

    public static string CustomPropertiesPath(string sessionDirectory)
        => Path.Combine(sessionDirectory, "metadata", "custom-properties.json");

    public static string MetricsDirectory(string sessionDirectory)
        => Path.Combine(sessionDirectory, "telemetry", "metrics");

    public static string EventsDirectory(string sessionDirectory)
        => Path.Combine(sessionDirectory, "telemetry", "events");

    public static string TouchesDirectory(string sessionDirectory)
        => Path.Combine(sessionDirectory, "input", "touches");

    public static string ScreenshotsDirectory(string sessionDirectory)
        => Path.Combine(sessionDirectory, "screenshots");

    public static string ScreenshotIndexDirectory(string sessionDirectory)
        => Path.Combine(sessionDirectory, "screenshots", "index");

    public static string AnnotationBundlesDirectory(string sessionDirectory)
        => Path.Combine(sessionDirectory, "annotations", "bundles");

    public static string AnnotationIndexPath(string sessionDirectory)
        => Path.Combine(sessionDirectory, "annotations", "index.jsonl");

    public static string CrashReportsDirectory(string sessionDirectory)
        => Path.Combine(sessionDirectory, "diagnostics", "crashes");

    public static string CrashReportPath(string sessionDirectory, string reportId)
        => Path.Combine(CrashReportsDirectory(sessionDirectory), $"{reportId}.json");

    public static string CrashTracePath(string sessionDirectory, string reportId, string extension = "trace")
        => Path.Combine(CrashReportsDirectory(sessionDirectory), $"{reportId}.{extension}");
}
