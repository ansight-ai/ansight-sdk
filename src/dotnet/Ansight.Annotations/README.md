# Ansight.Annotations

Opt-in, Debug-only in-app feedback capture for Ansight .NET apps.

The package presents a native annotation overlay on Android, iOS, and Mac Catalyst. When capture is requested, it immediately timestamps the annotation, freezes the current screenshot, and captures every registered visual-tree source before opening the editor. The built-in editor captures free-draw paths; Studio infers an approximate rectangle, oval, line, or arrow from each path for MCP consumers while preserving the original free draw. Inferred arrows include the perceived focal point at the arrow tip. One contextual text editor shows overall feedback when no path is selected and the selected path's text when one is selected. Existing paths can be selected, moved, resized, deleted, or restored with undo before saving. Host contribution hooks run before the result is sealed as a versioned `.ansightannotation` bundle. Screenshot and visual-tree entries retain their exact evidence-capture times, while the annotation and bundle retain the original request time regardless of how long the editor remains open.

`Ansight` and `Ansight.Maui` include this package, but neither enables it by default. The host app must call `WithAnnotatedFeedback()`, and the feature will initialize only when the consuming application was built with the `Debug` configuration. A Release build remains disabled even if the registration call is present.

## Setup

```csharp
using Ansight;
using Ansight.Annotations;

var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithAnnotatedFeedback();
    })
    .Build();

Runtime.InitializeAndActivate(options);
```

For MAUI, enable the feature in the `UseAnsight` callback:

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
var result = await Feedback.PresentAsync();
```

Native Android apps can pass the foreground activity explicitly. This is recommended when the app does not use MAUI:

```csharp
var result = await Feedback.PresentAsync(this);
```

A host-owned UI can bypass the built-in overlay and submit its own normalized shapes:

```csharp
var result = await Feedback.CaptureAsync(new AnnotationCaptureRequest
{
    Feedback = "The total overlaps the action button.",
    Shapes =
    [
        new AnnotationShape(AnnotationShapeKind.Rectangle, 0.62, 0.74, 0.31, 0.12)
    ]
});
```

## Hooks and artifacts

Hooks run after screenshot and visual-tree capture and before the bundle is sealed. A failing hook is recorded in the bundle and does not prevent other hooks or delivery from running.

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

When Studio is connected, the sealed bundle is submitted through the live session. When offline capture is active, it also stores bundles under `annotations/bundles` and appends `annotations/index.jsonl`. If no destination accepts the bundle, it is retained atomically in the local annotation outbox and the result status is `Queued`.
