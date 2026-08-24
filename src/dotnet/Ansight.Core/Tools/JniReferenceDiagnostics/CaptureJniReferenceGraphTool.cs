#if ANDROID
namespace Ansight.Tools.JniReferenceDiagnostics;

using System.Text.Json.Nodes;
using AI.Ansight.Dotnet;
using Android.App;

/// <summary>
/// Captures a bounded, redacted Android heap graph rooted at JNI references.
/// </summary>
public sealed class CaptureJniReferenceGraphTool : ITool
{
    private const int DefaultMaximumNodes = 512;
    private const int DefaultMaximumEdges = 1024;
    private const int DefaultMaximumDepth = 4;

    public string Category => "jni_references";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => JniReferenceDiagnosticsToolIds.CaptureGraph;

    public string Name => "Capture JNI Object-Reference Graph";

    public string Description => "Captures a bounded, redacted object graph rooted at JNI references.";

    public string Keywords => "jni java native references globals locals monitors heap graph diagnostics";

    public ToolSchema ArgumentsSchema => ToolSchema.Object(
        description: "Bounds for a JNI-rooted object-reference graph capture.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["maxNodes"] = ToolSchema.Integer("Maximum objects to return."),
            ["maxEdges"] = ToolSchema.Integer("Maximum reference edges to return."),
            ["maxDepth"] = ToolSchema.Integer("Maximum reference distance from a JNI root.")
        });

    public ToolSchema ResultSchema => ToolSchema.Object(
        description: "Bounded, redacted object-reference graph rooted at JNI references.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["schemaVersion"] = ToolSchema.String("Graph payload schema version."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for the heap snapshot.", format: "date-time"),
            ["jniRootCount"] = ToolSchema.Integer("Total JNI roots in the heap snapshot."),
            ["roots"] = ToolSchema.Array(ToolSchema.Object(additionalProperties: true)),
            ["nodes"] = ToolSchema.Array(ToolSchema.Object(additionalProperties: true)),
            ["edges"] = ToolSchema.Array(ToolSchema.Object(additionalProperties: true)),
            ["truncated"] = ToolSchema.Boolean("Whether any graph content was omitted.")
        },
        required:
        [
            "schemaVersion",
            "capturedAtUtc",
            "jniRootCount",
            "roots",
            "nodes",
            "edges",
            "truncated"
        ],
        additionalProperties: true);

    public ValueTask<ToolAvailability> GetAvailabilityAsync(ToolAvailabilityContext context)
    {
        var available = Application.Context is Application;
        return ValueTask.FromResult(
            available
                ? ToolAvailability.Available
                : ToolAvailability.Unavailable(
                    "android_application_unavailable",
                    "The Android application context is unavailable.",
                    requiredState: "Initialized Android application"));
    }

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return Task.Run(() =>
        {
            try
            {
                var application = Application.Context as Application
                    ?? throw new InvalidOperationException("The Android application context is unavailable.");
                var maximumNodes = ParseBound(arguments, "maxNodes", DefaultMaximumNodes, 1, 8192);
                var maximumEdges = ParseBound(arguments, "maxEdges", DefaultMaximumEdges, 1, 16384);
                var maximumDepth = ParseBound(arguments, "maxDepth", DefaultMaximumDepth, 0, 16);
                var json = AnsightDotNetBridge.CaptureJniReferenceGraph(
                    application,
                    maximumNodes,
                    maximumEdges,
                    maximumDepth)
                    ?? throw new InvalidDataException("The Android JNI graph collector returned no JSON.");
                var payload = JsonNode.Parse(json)
                    ?? throw new InvalidDataException("The Android JNI graph collector returned an empty payload.");
                return ToolResult.Success(payload);
            }
            catch (ArgumentException exception)
            {
                return ToolResult.Failure(
                    exception.Message,
                    errorCode: "jni_reference_graph_invalid_argument");
            }
            catch (Exception exception)
            {
                return ToolResult.Failure(
                    exception.Message,
                    errorCode: "jni_reference_graph_capture_failed");
            }
        });
    }

    private static int ParseBound(
        IReadOnlyDictionary<string, string> arguments,
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        if (!arguments.TryGetValue(name, out var rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(rawValue, out var value))
        {
            throw new ArgumentException($"Argument '{name}' must be an integer.", name);
        }

        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"Argument '{name}' must be between {minimum} and {maximum}.");
        }

        return value;
    }
}
#endif
