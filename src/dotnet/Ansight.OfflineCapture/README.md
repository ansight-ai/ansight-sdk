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

## Team upload

Team admins and owners can issue an app-scoped capture API key from the Ansight
portal. The secret is shown once and can be revoked without changing an app's
connection profile.

Stop the capture before uploading so the exported archive is immutable:

```csharp
await offlineCapture.StopAsync();

var result = await offlineCapture.UploadAsync(
    new OfflineCaptureUploadOptions
    {
        ApiKey = Environment.GetEnvironmentVariable("ANSIGHT_CAPTURE_API_KEY")!,
        Title = "Checkout regression"
    },
    new Progress<OfflineCaptureUploadProgress>(update =>
    {
        Console.WriteLine(
            $"{update.Stage}: {update.BytesTransferred}/{update.TotalBytes}");
    }));

Console.WriteLine(result.SessionUrl);
```

`OfflineCaptureUploadOptions.Endpoint` defaults to the hosted Ansight ingest
function and can be overridden for local development or self-hosting. Uploads
are sent to a one-archive signed storage URL; the app-scoped key is never sent
to object storage. The capture manifest must contain the same app/package ID
that was bound to the key at issuance. Transient API and storage failures are
retried with an idempotency key, and the temporary ZIP is removed after
completion or failure.

Data is written as compact JSONL in `.ansight/sessions/{sessionId}` with minified property names and append-only segment files. Offline capture uses the runtime retention period and `SessionJpegCapture` settings by default. Use the override properties only when offline capture needs behavior different from the active runtime configuration.

> **Important:** Offline screenshot capture will result in an FPS drop while
> frames are captured and encoded. Disable `SessionJpegCapture` or set
> `SessionJpegCaptureEnabledOverride = false` for performance-focused runs
> unless visual evidence is required.

## Activation

- `Disabled`: no automatic start.
- `Immediate`: starts capture now and persists `Disabled` for future sessions.
- `NextSessionOnly`: persists a future one-shot start. During `InitializeAsync`, capture starts and the persisted mode is cleared without stopping the active capture.
- `AlwaysOn`: starts capture for every app session until disabled.

The controller supports runtime mutation through `UpdateOptionsAsync(Action<OfflineCaptureOptions>)`. `ActivationMode`, retention limits, segment duration, and session JPEG overrides are mutable while capture is active. `RootDirectory` and `MaximumQueuedRecords` affect active writer ownership and queue construction, so changing either requires stopping capture first.

## Storage Layout

```text
.ansight/
  settings.json
  sessions/
    {sessionId}/
      manifest.json
      metadata/
        channels.json
        device-profile.json
        custom-properties.json
      telemetry/
        metrics/
          m-{utc}.jsonl
        events/
          e-{utc}.jsonl
      input/
        touches/
          t-{utc}.jsonl
      screenshots/
        {utc}.jpg
        index/
          s-{utc}.jsonl
      annotations/
        bundles/
          {annotationId}.ansightannotation
        index.jsonl
```

`settings.json`, `manifest.json`, and `metadata/*.json` are compact metadata files. High-volume data files are append-only JSONL segments. Metric, event, touch, and screenshot index records use minified JSON with short property names and no formatted whitespace.

When the Debug-only `Ansight.Annotations` feature is explicitly enabled, an active offline capture automatically registers as an annotation destination. Completed feedback bundles are written directly to `annotations/bundles`, indexed in `annotations/index.jsonl`, and included in ZIP export. Annotation writes are isolated from the bounded telemetry queue, so telemetry backpressure does not drop a submitted feedback bundle.

Retention is enforced during startup, active writes, runtime option updates, and export preparation. Time retention deletes closed files older than the effective retention window; active writer files are never deleted. `MaximumSessionBytes` trims old closed files inside the active session, and `MaximumRetainedBytes` trims old closed files across `.ansight`.

## Export

The SDK supports both file and stream export:

- `ExportToFileAsync(path, options)` writes a ZIP file and returns the file path.
- `ExportToStreamAsync(stream, options)` writes a ZIP to a caller-provided stream.
- `OfflineCaptureExportOptions.Password` enables AES-256 entry encryption through SharpZipLib on current `net9.0` targets.
- Without a password, export uses `System.IO.Compression.ZipArchive`.

ZIP exports stream the raw `.ansight` session files directly. Export does not expand the captured JSONL into Studio archive JSON; Studio ingests the minified JSONL capture format directly. `IncludeStudioSessionArchive` and `IncludeRawCaptureFiles` are retained for source compatibility, but export no longer expands JSONL capture files into Studio-native aggregate JSON.

## Samples

The primary manual test app is `src/dotnet/samples/Ansight.OfflineCapture.MauiSample`. It exercises capture activation, runtime mutation, touch/screenshot capture, lifecycle events, custom session properties, and ZIP export from a real .NET MAUI app.

Run the local desktop target with:

```bash
dotnet build src/dotnet/samples/Ansight.OfflineCapture.MauiSample -f net9.0-maccatalyst
dotnet run --project src/dotnet/samples/Ansight.OfflineCapture.MauiSample -f net9.0-maccatalyst
```

A console smoke sample is also available at `src/dotnet/samples/Ansight.OfflineCapture.Sample`:

```bash
dotnet run --project src/dotnet/samples/Ansight.OfflineCapture.Sample
dotnet run --project src/dotnet/samples/Ansight.OfflineCapture.Sample -- --password=secret
```
