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

## Optional cloud integration

Capture and ZIP export require no Ansight account. Team upload is supplied by the separate private `Ansight.Cloud.OfflineCapture` integration assembly. Apps that opt into that service explicitly reference it alongside this package; ordinary capture apps have no cloud dependency.

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

## Connectivity evidence

Add `WithConnectivityCapture()` from `Ansight.Connectivity` to the app's existing runtime options before starting offline capture. The all-in-one `Ansight` and `Ansight.Maui` packages include this optional collector; core-only apps reference `Ansight.Connectivity` explicitly. Collection remains opt-in.

The offline recorder retains connection snapshots, changes, supported quality estimates, and observation gaps without a host connection. Starting a capture includes the last known observation with its original timestamp. Segments and retention preserve preceding state, while queue overflow records a gap and increments the manifest's dropped-record count. No connectivity index is written when no observations were captured.

See [Connectivity capture](../../../docs/connectivity.md) for setup and platform measurements. For native Apple/Android recording and automatic reconnect transfer through every mobile SDK, use [native offline capture](../../../docs/native-offline-capture-implementation.md), implemented after SDK 1.8.0. This managed controller remains available for compatibility and portable .NET; do not run it alongside the native recorder.

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
      network/
        requests/
          {utc}-{requestId}.json
      connectivity/
        index.json
        segments/
          000001.jsonl
      screenshots/
        {utc}.jpg
        index/
          s-{utc}.jsonl
      annotations/
        bundles/
          {annotationId}.ansightannotation
        index.jsonl
      diagnostics/
        crashes/
          {crashReportId}.json
          {crashReportId}.trace
```

`settings.json`, `manifest.json`, and `metadata/*.json` are compact metadata files. High-volume data files are append-only JSONL segments. Metric, event, touch, and screenshot index records use minified JSON with short property names and no formatted whitespace. Network requests are individual `ansight.network-request.v1` JSON documents so CLI and playback consumers can inspect them without reading a combined blob. They retain the runtime's sanitized records, including bodies when the configured network-capture policy allows them. Configure body capture and redaction before recording.

If the process terminates while capture is active, the native runtime recovers
the crash on the next launch, finds this session by `ProcessSessionId`, stores
the normalized report and any bounded OS trace under `diagnostics/crashes`, and
sets `StoppedAtUtc`, `TerminationKind`, and `CrashReportIds` in the manifest.
The recovered files are included automatically by both ZIP export and offline
upload. Normal `StopAsync` seals the manifest with `TerminationKind: normal`.

When the Debug-only `Ansight.Annotations` feature is explicitly enabled, an active offline capture automatically registers as an annotation destination. Completed feedback bundles are written directly to `annotations/bundles`, indexed in `annotations/index.jsonl`, and included in ZIP export. Annotation writes are isolated from the bounded telemetry queue, so telemetry backpressure does not drop a submitted feedback bundle.

Retention is enforced during startup, active writes, runtime option updates, and export preparation. Time retention deletes closed files older than the effective retention window; active writer files are never deleted. `MaximumSessionBytes` trims old closed files inside the active session, and `MaximumRetainedBytes` trims old closed files across `.ansight`.

## Export

The SDK supports both file and stream export:

- `ExportToFileAsync(path, options)` writes a ZIP file and returns the file path.
- `ExportToStreamAsync(stream, options)` writes a ZIP to a caller-provided stream.
- `OfflineCaptureExportOptions.Password` enables AES-256 entry encryption through SharpZipLib on current `net9.0` targets.
- Without a password, export uses `System.IO.Compression.ZipArchive`.

ZIP exports stream the raw `.ansight` session files directly. Export does not expand the captured JSONL into host archive JSON; host ingests the minified JSONL capture format directly. `IncludeRawCaptureFiles` is retained for source compatibility, but export no longer expands JSONL capture files into host-native aggregate JSON.

## Import and review

Copy the exported ZIP to the developer machine. Start the host with `ansight host run --open`, then import it from another terminal:

```sh
ansight session import capture.zip --json
ansight serve --open --session <session-id>
ansight session connectivity <session-id> summary --json
ansight session connectivity <session-id> at --offset 01:15 --json
```

Use the session ID returned by import. For encrypted exports, set `ANSIGHT_CAPTURE_PASSWORD` in the environment and add `--password-env ANSIGHT_CAPTURE_PASSWORD` to the import command. The host reads the raw offline ZIP directly, including retained connectivity and annotations. Open the player's Connectivity tab to correlate changes with screenshots, logs, and requests. Capture, export, import, and local review require no account.

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
