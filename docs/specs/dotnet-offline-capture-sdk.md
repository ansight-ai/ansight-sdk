# .NET Offline Capture SDK Spec

## Summary

The .NET offline capture SDK records Ansight runtime data to an app-local `.ansight` folder without requiring a live host connection. Capture can be started and stopped at runtime, configured for one future app session, or configured to start for every app session until disabled. Active capture settings can be mutated while capture is running.

The implementation lives in `Ansight.OfflineCapture`, with minimal internal hooks from `Ansight.Core` for touch and JPEG capture feeds. `Ansight.Core` remains the owner of telemetry, touch, screenshot, lifecycle, and host-pairing primitives; the offline package owns disk persistence, retention, export, and ZIP encryption dependencies. Offline capture uses the active runtime options as its default capture policy; offline options only add storage/export policy and explicit overrides when offline behavior must differ from runtime behavior.

## Package Boundary

- `Ansight.Core`
  - Owns runtime telemetry feeds and in-process capture primitives.
  - Grants `Ansight.OfflineCapture` internal access to existing runtime capture feeds.
  - Exposes an internal JPEG-frame capture helper so offline storage can save JPEG bytes without opening a WebSocket transport.
- `Ansight.OfflineCapture`
  - Owns `.ansight` storage layout.
  - Owns activation settings and runtime capture controller.
  - Owns append-only writers, retention cleanup, and ZIP export.
  - References SharpZipLib for password-protected ZIP export on current `net9.0` targets.
- `Ansight`
  - References `Ansight.OfflineCapture` so all-in-one consumers can use the SDK without an extra package reference.

## Public API

```csharp
var offlineCapture = OfflineCapture.Configure(new OfflineCaptureOptions
{
    RootDirectory = ".ansight",
    MaximumSessionBytes = 128 * 1024 * 1024,
    MaximumRetainedBytes = 512 * 1024 * 1024
});

await offlineCapture.InitializeAsync();
await offlineCapture.StartAsync();

await offlineCapture.UpdateOptionsAsync(options =>
{
    options.RetentionWindowOverride = TimeSpan.FromSeconds(30);
    options.MaximumSessionBytes = 64 * 1024 * 1024;
    options.SessionJpegCaptureEnabledOverride = false;
});

await offlineCapture.ExportToFileAsync(
    "capture.zip",
    new OfflineCaptureExportOptions { Password = "optional-password" });

await offlineCapture.StopAsync();
```

### Activation Modes

- `Disabled`: no automatic start.
- `Immediate`: starts capture now and persists `Disabled` for future sessions.
- `NextSessionOnly`: persists a future one-shot start. During `InitializeAsync`, capture starts and the persisted mode is cleared to `Disabled` without stopping the active capture.
- `AlwaysOn`: persists automatic start for every app session until disabled.

### Runtime Mutation

The controller supports runtime mutation through `UpdateOptionsAsync(Action<OfflineCaptureOptions>)`.

Mutable while active:

- `ActivationMode`
- `RetentionWindowOverride`
- `MaximumSessionBytes`
- `MaximumRetainedBytes`
- `SegmentDuration`
- `SessionJpegCaptureEnabledOverride`
- `SessionJpegCaptureOverride`

Not mutable while active:

- `RootDirectory`
- `MaximumQueuedRecords`

Those two values affect active writer ownership and queue construction. Changing either requires stopping capture first.

### Runtime Option Reuse

Offline capture intentionally does not duplicate the runtime capture options.

- Retention defaults to `RuntimeImpl.Options.RetentionPeriodSeconds`.
- JPEG capture defaults to `RuntimeImpl.Options.SessionJpegCapture`.
- If runtime JPEG capture is null, offline JPEG capture is disabled unless `SessionJpegCaptureEnabledOverride` is set to `true`.
- `RetentionWindowOverride`, `SessionJpegCaptureEnabledOverride`, and `SessionJpegCaptureOverride` exist only for cases where offline capture needs a different retention or JPEG policy from the active runtime.
- Storage policy remains offline-specific because it has no live-runtime equivalent: root directory, retained bytes, segment duration, writer queue size, activation mode, and export options.

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
```

`settings.json`, `manifest.json`, and `metadata/*.json` are compact metadata files. High-volume data files are append-only JSONL segments.

## Data Encoding

All data-point records are minified JSON with short property names and no formatted whitespace.

Metric record:

```json
{"t":1782734400000,"c":3,"v":60}
```

- `t`: captured UTC Unix milliseconds
- `c`: channel id
- `v`: value

Event record:

```json
{"id":"0197...","t":1782734400000,"c":255,"k":0,"l":"checkout","d":"cart"}
```

- `id`: compact event id
- `t`: captured UTC Unix milliseconds
- `c`: channel id
- `k`: event type integer
- `l`: label
- `d`: optional details

Touch record:

```json
{"id":"0197...","t":1782734400000,"a":0,"p":1,"x":42,"y":64,"w":390,"h":844,"u":"pt","s":3}
```

- `id`: compact touch id
- `t`: captured UTC Unix milliseconds
- `a`: action integer
- `p`: pointer id
- `i`: pointer index when non-zero
- `pc`: pointer count when not one
- `x`, `y`: captured coordinate
- `w`, `h`: surface size
- `u`: coordinate unit
- `s`: surface scale

Screenshot index record:

```json
{"t":1782734400000,"p":"screenshots/20260630010101000.jpg","w":480,"h":1040,"q":60,"b":32124}
```

- `t`: captured UTC Unix milliseconds
- `p`: session-relative JPEG path
- `w`, `h`: dimensions
- `q`: JPEG quality
- `b`: JPEG byte count

## Performance Requirements

- Capture callbacks do not rewrite aggregate files.
- Data points are queued into a bounded channel and written by one background writer.
- Segment files are append-only and rotate by `SegmentDuration`.
- High-volume records are compact JSONL with minified property names.
- Export streams existing files into a ZIP entry-by-entry.
- Retention deletes whole expired segments when possible and never reloads a large capture file to remove old entries.

## Retention

Retention is enforced during startup, active writes, runtime option updates, and export preparation. By default the time window is the runtime retention period. `RetentionWindowOverride` can narrow or widen the offline rolling window without changing the runtime data sink.

- Time retention deletes closed files older than `UtcNow - effective retention window`.
- Active writer files are never deleted.
- `MaximumSessionBytes` trims old closed files inside the active session.
- `MaximumRetainedBytes` trims old closed files across `.ansight`.
- Empty session directories are removed best-effort.

## Export

The SDK supports both file and stream export.

- `ExportToFileAsync(path, options)` writes a ZIP file and returns the file path.
- `ExportToStreamAsync(stream, options)` writes a ZIP to a caller-provided stream.
- `OfflineCaptureExportOptions.Password` enables AES-256 entry encryption through SharpZipLib on `net9.0`.
- Without a password, export uses `System.IO.Compression.ZipArchive`.
- Export streams the raw `.ansight` session files directly.
- `IncludeStudioSessionArchive` and `IncludeRawCaptureFiles` are retained for source compatibility, but export no longer expands JSONL capture files into Studio-native aggregate JSON.

Default ZIP layout:

```text
.ansight/
  sessions/
    {sessionId}/
      manifest.json
      metadata/
      telemetry/
      input/
      screenshots/
```

Studio import reads the minified JSONL and metadata files from the raw `.ansight` session layout. App events remain in `.ansight/telemetry/events/*.jsonl`, metrics remain in `.ansight/telemetry/metrics/*.jsonl`, touches remain in `.ansight/input/touches/*.jsonl`, and screenshot references remain in `.ansight/screenshots/index/*.jsonl`.

## Samples

The primary manual test app is a .NET MAUI sample located at:

```text
src/dotnet/samples/Ansight.OfflineCapture.MauiSample
```

Run the local desktop target with:

```bash
dotnet build src/dotnet/samples/Ansight.OfflineCapture.MauiSample -f net9.0-maccatalyst
dotnet run --project src/dotnet/samples/Ansight.OfflineCapture.MauiSample -f net9.0-maccatalyst
```

The MAUI sample starts the Ansight MAUI runtime, enables runtime JPEG capture, starts/stops offline capture, mutates capture options while active, records metrics/events/screen views/touches/lifecycle changes/custom properties, applies memory pressure, exports optional password ZIP files, and shows the active session file list.

A console smoke sample is also available at:

```text
src/dotnet/samples/Ansight.OfflineCapture.Sample
```

Run the console smoke sample with:

```bash
dotnet run --project src/dotnet/samples/Ansight.OfflineCapture.Sample
dotnet run --project src/dotnet/samples/Ansight.OfflineCapture.Sample -- --password=secret
```

The console sample starts Ansight, starts offline capture, records sample metrics/events, mutates retention settings at runtime, exports a ZIP, and stops capture.

## Acceptance Criteria

- Capture can start and stop at runtime.
- Capture can be configured for immediate, next-session-only, always-on, or disabled activation.
- Active capture properties can be mutated at runtime.
- Captured metrics, events, touches, and screenshot indexes are minified append-only JSONL records.
- Screenshot JPEGs are written as files and indexed with minified JSONL metadata.
- Retention is enforced by time and size without large file reload/rewrite.
- Export supports file-path and stream destinations.
- Export supports optional password protection.
- Default exports preserve the raw `.ansight` session layout for Studio ingestion, replay, and analysis.
- A sample app demonstrates the SDK.
- Unit tests cover minified data, runtime mutation, activation mode, raw archive entries, and ZIP export.
