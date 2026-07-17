using System.Text.Json.Nodes;
using Ansight.Annotations;
using Ansight.Artifacts;

namespace Ansight.TestHarness;

internal sealed class HarnessAnnotationCaptureHook : IAnnotationCaptureHook
{
    public ValueTask ContributeAsync(
        AnnotationCaptureContext context,
        CancellationToken cancellationToken)
    {
        var harnessState = new JsonObject
        {
            ["application"] = "Ansight.TestHarness",
            ["runtimeActive"] = Runtime.IsActive,
            ["capturedAtUtc"] = context.CapturedAtUtc.ToString("O")
        };

        context.AddCustomData("harness", harnessState);
        context.AddArtifact(new AnnotationArtifact(
            "Harness state",
            "application/json",
            "harness-state.json",
            ArtifactPayload.FromText(harnessState.ToJsonString()))
        {
            Description = "State injected by the .NET MAUI annotated-feedback test harness.",
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "annotation-capture-hook"
            }
        });

        return ValueTask.CompletedTask;
    }
}
