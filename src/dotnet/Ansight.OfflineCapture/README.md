# Ansight.OfflineCapture

Offline capture storage, retention, runtime mutation, and ZIP export support for Ansight .NET apps.

```csharp
using Ansight.OfflineCapture;

var offlineCapture = OfflineCapture.Configure(new OfflineCaptureOptions
{
    RootDirectory = ".ansight",
    MaximumSessionBytes = 128 * 1024 * 1024
});

await offlineCapture.InitializeAsync();
await offlineCapture.StartAsync();

await offlineCapture.UpdateOptionsAsync(options =>
{
    options.RetentionWindowOverride = TimeSpan.FromSeconds(30);
    options.SessionJpegCaptureEnabledOverride = false;
});

await offlineCapture.ExportToFileAsync("capture.zip", new OfflineCaptureExportOptions
{
    Password = "optional-password"
});

await offlineCapture.StopAsync();
```

High-volume data is written as compact JSONL in `.ansight/sessions/{sessionId}` with minified property names and append-only segment files.

ZIP exports include Ansight Studio session archive entries by default (`session.json`, `session-data/*.json`, and `session-images/*`) plus the raw `.ansight` files, so the exported capture can be imported into Studio for replay and analysis.

Offline capture uses the runtime retention period and `SessionJpegCapture` settings by default. Use the override properties only when offline capture needs behavior different from the active runtime configuration.

The primary manual test app is `src/dotnet/samples/Ansight.OfflineCapture.MauiSample`. It exercises capture activation, runtime mutation, touch/screenshot capture, lifecycle events, custom session properties, and ZIP export from a real .NET MAUI app.

See `docs/specs/dotnet-offline-capture-sdk.md` for the full storage and API spec.
