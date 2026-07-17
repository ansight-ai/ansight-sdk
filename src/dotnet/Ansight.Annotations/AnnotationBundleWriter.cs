namespace Ansight.Annotations;

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

internal static class AnnotationBundleWriter
{
    private const string BundleSchema = "ansight.annotation.bundle.v1";

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    internal static async Task<AnnotationBundle> CreateAsync(
        Guid annotationId,
        DateTimeOffset requestedAtUtc,
        AnnotationCaptureRequest request,
        AnnotationEvidenceSnapshot evidence,
        AnnotationCaptureContext context,
        long maximumArtifactBytes,
        CancellationToken cancellationToken)
    {
        using var bundleStream = new MemoryStream();
        using (var archive = new ZipArchive(bundleStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AnnotationScreenshotManifest? screenshotManifest = null;
            if (evidence.Screenshot is not null)
            {
                const string screenshotPath = "evidence/screenshot.jpg";
                await WriteBytesAsync(archive, screenshotPath, evidence.Screenshot.Bytes, cancellationToken);
                screenshotManifest = new AnnotationScreenshotManifest
                {
                    Path = screenshotPath,
                    MimeType = evidence.Screenshot.MimeType,
                    Width = evidence.Screenshot.Width,
                    Height = evidence.Screenshot.Height,
                    CapturedAtUtc = evidence.Screenshot.CapturedAtUtc
                };
            }

            var visualTreeManifests = new List<AnnotationVisualTreeManifest>();
            foreach (var visualTree in evidence.VisualTrees)
            {
                var safeSource = SanitizePathSegment(visualTree.Source);
                var path = $"evidence/visual-trees/{safeSource}.json";
                await WriteTextAsync(archive, path, visualTree.Json, cancellationToken);
                visualTreeManifests.Add(new AnnotationVisualTreeManifest
                {
                    Source = visualTree.Source,
                    DisplayName = visualTree.DisplayName,
                    Path = path,
                    CapturedAtUtc = visualTree.CapturedAtUtc,
                    Truncated = visualTree.Truncated
                });
            }

            var artifactManifests = new List<AnnotationArtifactManifest>();
            for (var index = 0; index < context.Artifacts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var artifact = context.Artifacts[index];
                var artifactManifest = await WriteArtifactAsync(
                    archive,
                    artifact,
                    index,
                    maximumArtifactBytes,
                    cancellationToken);
                artifactManifests.Add(artifactManifest);
            }

            var customData = new JsonObject();
            foreach (var item in context.CustomData)
            {
                customData[item.Key] = item.Value?.DeepClone();
            }

            var manifest = new AnnotationBundleManifest
            {
                Schema = BundleSchema,
                Version = 1,
                AnnotationId = annotationId,
                CaptureGroupId = evidence.CaptureGroupId,
                CapturedAtUtc = requestedAtUtc,
                Feedback = string.IsNullOrWhiteSpace(request.Feedback) ? null : request.Feedback.Trim(),
                Shapes = request.Shapes.ToArray(),
                Screenshot = screenshotManifest,
                VisualTrees = visualTreeManifests,
                Evidence = evidence.Results,
                CustomData = customData,
                HookFailures = context.HookFailures,
                Artifacts = artifactManifests
            };
            await WriteJsonAsync(archive, "manifest.json", manifest, cancellationToken);
        }

        return new AnnotationBundle(annotationId, requestedAtUtc, bundleStream.ToArray());
    }

    private static async Task<AnnotationArtifactManifest> WriteArtifactAsync(
        ZipArchive archive,
        AnnotationArtifact artifact,
        int index,
        long maximumArtifactBytes,
        CancellationToken cancellationToken)
    {
        var manifest = new AnnotationArtifactManifest
        {
            Name = artifact.Name,
            Kind = artifact.Kind,
            MimeType = artifact.MimeType,
            FileName = artifact.FileName,
            Description = artifact.Description,
            Metadata = artifact.Metadata
        };

        try
        {
            if (artifact.Payload.SizeBytes > maximumArtifactBytes)
            {
                manifest.Status = AnnotationEvidenceStatus.Skipped;
                manifest.Reason = $"Artifact exceeded the {maximumArtifactBytes:N0}-byte capture limit.";
                manifest.SizeBytes = artifact.Payload.SizeBytes;
                return manifest;
            }

            await using var source = await artifact.Payload.OpenReadAsync(cancellationToken);
            var bytes = await ReadWithLimitAsync(source, maximumArtifactBytes, cancellationToken);
            var safeFileName = SanitizePathSegment(Path.GetFileName(artifact.FileName));
            var path = $"artifacts/{index:D3}-{safeFileName}";
            await WriteBytesAsync(archive, path, bytes, cancellationToken);
            manifest.Path = path;
            manifest.SizeBytes = bytes.LongLength;
            manifest.Status = AnnotationEvidenceStatus.Captured;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            manifest.Status = AnnotationEvidenceStatus.Failed;
            manifest.Reason = exception.Message;
        }

        return manifest;
    }

    private static async Task<byte[]> ReadWithLimitAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidOperationException($"Artifact exceeded the {maximumBytes:N0}-byte capture limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return destination.ToArray();
    }

    private static async Task WriteJsonAsync<T>(
        ZipArchive archive,
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, jsonOptions, cancellationToken);
    }

    private static async Task WriteTextAsync(
        ZipArchive archive,
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, leaveOpen: false);
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(value);
    }

    private static async Task WriteBytesAsync(
        ZipArchive archive,
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static string SanitizePathSegment(string value)
    {
        var sanitized = new string(value
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
    }

    private sealed class AnnotationBundleManifest
    {
        public string Schema { get; set; } = string.Empty;
        public int Version { get; set; }
        public Guid AnnotationId { get; set; }
        public Guid CaptureGroupId { get; set; }
        public DateTimeOffset CapturedAtUtc { get; set; }
        public string? Feedback { get; set; }
        public IReadOnlyList<AnnotationShape> Shapes { get; set; } = Array.Empty<AnnotationShape>();
        public AnnotationScreenshotManifest? Screenshot { get; set; }
        public IReadOnlyList<AnnotationVisualTreeManifest> VisualTrees { get; set; } = Array.Empty<AnnotationVisualTreeManifest>();
        public IReadOnlyList<AnnotationEvidenceResult> Evidence { get; set; } = Array.Empty<AnnotationEvidenceResult>();
        public JsonObject CustomData { get; set; } = new();
        public IReadOnlyList<string> HookFailures { get; set; } = Array.Empty<string>();
        public IReadOnlyList<AnnotationArtifactManifest> Artifacts { get; set; } = Array.Empty<AnnotationArtifactManifest>();
    }

    private sealed class AnnotationScreenshotManifest
    {
        public string Path { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTimeOffset CapturedAtUtc { get; set; }
    }

    private sealed class AnnotationVisualTreeManifest
    {
        public string Source { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTimeOffset CapturedAtUtc { get; set; }
        public bool Truncated { get; set; }
    }

    private sealed class AnnotationArtifactManifest
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public AnnotationEvidenceStatus Status { get; set; }
        public string? Reason { get; set; }
        public string? Path { get; set; }
        public long? SizeBytes { get; set; }
    }
}
