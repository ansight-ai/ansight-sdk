# Telemetry: values, channels, and spans

## Numeric values, units, and channel IDs

Metrics accept finite numbers, including fractions such as `16.67` and `0.125`.
They are preserved through native bridges, live sessions, offline archives, and
replay. NaN and infinity are rejected. Values use double precision; integers
above 2^53 are not guaranteed to remain exact. Existing integer calls continue to work.

Declare `unit` on the channel (for example `ms`, `bytes`, `%`, or `count`). `group`
organizes related channels, while `type`, `source`, and `kind` describe their
meaning. The player offers separate metric lanes with compatible units, and
separate event/span lanes by channel. Units are metadata; callers must send values
in the declared unit.

Use **64–254** for portable application channels. This is the recommended range,
not a requirement to renumber existing custom channels.

| IDs | Owner / meaning |
| --- | --- |
| 0 | Managed/Java heap, depending on the SDK |
| 1 | Native heap on Android; physical footprint on Apple |
| 2 | Android RSS; reserved on every stack |
| 3 | Frames per second |
| 4 | App lifecycle |
| 5 | Battery level |
| 6 | JNI reference diagnostics |
| 7 | Open file handles |
| 32–33 | React Native JavaScript heap used/total; avoid for app channels |
| 40–43 | Flutter build/raster/total frame duration and frame count; avoid for app channels |
| 255 | Unspecified channel; valid for recording events, not custom registration |

Core registration rejects 0–7 and 255. Framework collectors own their IDs when
installed. Register a channel before recording metrics against it; existing
unknown-channel behavior is unchanged.

## Timed spans (zones)

A span names an operation and records its start and finish. A *zone* is the same
concept; there is one span API and one recording contract. Each span has a stable
ID, an optional explicit parent ID, a channel, a UTC start time, a monotonic
elapsed duration in milliseconds, and an outcome (`ok`, `error`, or `cancelled`).
Explicit parents work across threads and asynchronous operations. End is
idempotent. A start without an end remains **incomplete** in replay; shutdown,
crashes, or retention must not manufacture a successful ending. Scope disposal
means success unless the caller explicitly marks failure; use the measured
operation helpers to record thrown errors automatically.

Spans use the existing application event stream, with event type `Span` and JSON
in `details`: `spanId`, `parentSpanId`, `phase` (`start`/`end`), `startedAtUtc`,
`durationMs` (end only), and `status` (end only). End records repeat their start
metadata so cropped or reordered recordings can still reconstruct a completed
span. This adds fields/event data to the existing format; it introduces no
second session schema or version negotiation.

The host's telemetry timeline query includes `matchedSpanCount`, `spans`, and
`spansTruncated` (at most 500 returned spans). Channel/time filters apply; span
ranges overlap the query interval. Event records remain available independently.

### Inspect spans with CLI tasks

In a workspace task, query `await ansight.telemetry.getTimeline({ types: ['spans'] })`.
It returns reconstructed spans with IDs, parents, start times, durations, and
outcomes. Run the task with `ansight task run <task-id> --app-id <app-id>
--repository <workspace> --session-id <connected-session-id> --json`.
The underlying host/MCP operation is `ansight_get_telemetry_timeline`.

There is currently no standalone `ansight session spans` command. The CLI task
runner requires a connected app; saved sessions can be inspected in the player's
**Telemetry → Spans** view. See the [telemetry review guide](https://www.ansight.ai/docs/local-player/telemetry-review)
for a complete task module, CLI commands, filters, and result fields.

## dotnet

```csharp
var channel = new Ansight.Telemetry.Channels.Channel(64, "Frame", System.Drawing.Color.CornflowerBlue,
    unit: "ms", group: "Render");
// Add channel through Options.CreateBuilder().WithAdditionalChannels(new[] { channel }).
Ansight.Runtime.Metric(16.67, 64);
using (var frame = Ansight.Runtime.BeginSpan("Frame", 64))
{
    using var upload = Ansight.Runtime.BeginSpan("Upload", 64, frame.Id);
    // Work here. Call upload.Fail() if it failed.
}
var result = await Ansight.Runtime.MeasureAsync("Fetch", () => FetchAsync(), 64);
```

## android

```kotlin
AnsightRuntime.registerMetricChannel(AnsightChannel(64, "Frame", unit = "ms", group = "Render"))
AnsightRuntime.metric(16.67, 64)
AnsightRuntime.measure("Frame", 64) { frame ->
    AnsightRuntime.beginSpan("Upload", 64, frame.id).use { upload ->
        // Work here. Call upload.fail() if it failed.
    }
}
// For async work, keep a beginSpan handle and end it when the operation finishes.
```

## ios

```swift
try AnsightRuntime.shared.registerMetricChannel(AnsightChannel(id: 64, name: "Frame", unit: "ms", group: "Render"))
try AnsightRuntime.shared.metric(16.67, channel: 64)
try AnsightRuntime.shared.measure("Frame", channel: 64) { frame in
    let upload = try AnsightRuntime.shared.beginSpan("Upload", channel: 64, parentSpanId: frame.id)
    defer { try? upload.end() }
    // Work here. Call try upload.end(status: "error") if it failed.
}
// measure also accepts an async throwing closure. AnsightMetricStream.floating
// accepts a sampler returning Double?; existing integer samplers remain supported.
```

## react-native

```typescript
await Ansight.registerMetricChannel({ id: 64, name: "Frame", unit: "ms", group: "Render" });
await Ansight.metric(16.67, 64);
await Ansight.measure("Frame", async frame => {
  const upload = await Ansight.beginSpan("Upload", { channel: 64, parentSpanId: frame.id });
  try { await uploadWork(); }
  catch (error) { await upload.end("error"); throw error; }
  finally { await upload.end(); }
}, { channel: 64 });
```

## cordova

```typescript
import { registerMetricChannel, metric, measure, beginSpan } from "@ansight/capacitor";
await registerMetricChannel({ id: 64, name: "Frame", unit: "ms", group: "Render" });
await metric(16.67, 64);
await measure("Frame", async frame => {
  const upload = await beginSpan("Upload", { channel: 64, parentSpanId: frame.id });
  try { await uploadWork(); }
  catch (error) { await upload.end("error"); throw error; }
  finally { await upload.end(); }
}, { channel: 64 });
```

## flutter

```dart
await ansight.registerMetricChannel(const AnsightChannel(id: 64, name: 'Frame', unit: 'ms', group: 'Render'));
await ansight.metric(16.67, channel: 64);
await ansight.measure('Frame', (frame) async {
  final upload = await ansight.beginSpan('Upload', channel: 64, parentSpanId: frame.id);
  try { await uploadWork(); }
  catch (_) { await upload.end(status: 'error'); rethrow; }
  finally { await upload.end(); }
}, channel: 64);
```
