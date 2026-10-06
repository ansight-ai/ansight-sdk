# Ansight.Annotations

Debug-only in-app annotation capture for Ansight .NET apps.

`Annotate.PresentAsync()` uses the annotation engine in the native Android and Apple core packages. It timestamps the request, captures a screenshot and registered visual trees before opening the editor, and seals a versioned `.ansightannotation` bundle. The native editor supports free draw, undo, clear, and overall feedback text. The bundle is retained in a native outbox until it can be sent over the active host session. The host can infer approximate shapes from free-draw paths.

`Ansight` and `Ansight.Maui` include this standalone package. Their default builders register annotation capture automatically for Debug application builds. `WithAnnotatedFeedback(...)` customizes screenshot and visual-tree capture. A Release application build remains disabled.

## Setup

```csharp
using Ansight;
using Ansight.Annotations;

var options = Options.CreateBuilder()
    .WithAnsightSdk()
    .Build();

Runtime.InitializeAndActivate(options);
```

For MAUI, `UseAnsight` registers the Debug default. Configure it in the callback when needed:

```csharp
using Ansight.Annotations;
using Ansight.Maui;

builder.UseAnsight<App>(ansight =>
{
    ansight.WithAnnotatedFeedback();
});
```

Trigger the built-in overlay from app UI:

```csharp
var result = await Annotate.PresentAsync();
```

Native Android apps can pass the foreground activity explicitly. This is recommended when the app does not use MAUI:

```csharp
var result = await Annotate.PresentAsync(this);
```

A host-owned UI can bypass the built-in overlay and submit its own normalized shapes:

```csharp
var result = await Annotate.CaptureAsync(new AnnotationCaptureRequest
{
    Feedback = "The total overlaps the action button.",
    Shapes =
    [
        new AnnotationShape(AnnotationShapeKind.Rectangle, 0.62, 0.74, 0.31, 0.12)
    ]
});
```

## Hooks and artifacts

Hooks run for the managed `Annotate.CaptureAsync(request)` path after evidence capture and before its bundle is sealed. The native `PresentAsync()` path does not currently invoke managed hooks or managed offline sinks.

```csharp
using System.Text.Json.Nodes;
using Ansight.Annotations;
using Ansight.Artifacts;

sealed class FeedbackContextHook : IAnnotationCaptureHook
{
    public ValueTask ContributeAsync(
        AnnotationCaptureContext context,
        CancellationToken cancellationToken)
    {
        context.AddCustomData("account", JsonValue.Create("example"));
        context.AddArtifact(new AnnotationArtifact(
            "Navigation state",
            "application/json",
            "navigation.json",
            ArtifactPayload.FromText("{\"route\":\"/checkout\"}")));
        return ValueTask.CompletedTask;
    }
}

ansight.WithAnnotatedFeedback(annotations =>
{
    annotations.AddHook(new FeedbackContextHook());
});
```

Use `WithEvidencePolicy(...)` to deny individual evidence sources. Screenshot, visual-tree provider, hook, artifact, outbox, and destination failures are isolated and represented as results where possible; a missing or disallowed source does not abort the rest of the annotation.

## Visual trees and delivery

Annotation capture queries `VisualTreeProviderRegistry` at capture time and captures every registered source. The built-in `native` provider is always present. `Ansight.Maui` also registers the `maui` provider independently of whether remote tools are enabled.

When the host is connected, the sealed bundle is submitted through the live session. The native `PresentAsync()` path retains undelivered bundles in a native outbox and retries on reconnection. The managed `CaptureAsync(request)` path also supports offline-capture sinks, which store bundles under `annotations/bundles` and append `annotations/index.jsonl`.

`Feedback` is obsolete and forwards to `Annotate` for existing callers.
