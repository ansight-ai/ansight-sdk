namespace Ansight.Annotations;

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class AnnotationOutbox
{
    private const long MaximumPendingBundleBytes = 512L * 1024 * 1024;
    private readonly string directory;

    internal AnnotationOutbox(string? configuredDirectory)
    {
        directory = ResolveDirectory(configuredDirectory);
    }

    internal async Task<string> StoreAsync(AnnotationBundle bundle, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var destinationPath = Path.Combine(directory, bundle.FileName);
        var temporaryPath = Path.Combine(directory, $".{bundle.AnnotationId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(bundle.Bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal void Remove(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            Logger.Warning($"Unable to remove delivered annotation '{path}': {exception.Message}");
        }
    }

    internal async Task<IReadOnlyList<PendingAnnotationBundle>> LoadPendingAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<PendingAnnotationBundle>();
        }

        var pending = new List<PendingAnnotationBundle>();
        foreach (var path in Directory
            .EnumerateFiles(directory, "*.ansightannotation", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var file = new FileInfo(path);
                if (file.Length <= 0 || file.Length > MaximumPendingBundleBytes)
                {
                    Logger.Warning($"Ignoring annotation outbox bundle '{path}' because its size is invalid.");
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                using var stream = new MemoryStream(bytes, writable: false);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                var manifestEntry = archive.GetEntry("manifest.json")
                    ?? throw new InvalidDataException("The annotation bundle has no manifest.");
                await using var manifestStream = manifestEntry.Open();
                var manifest = await JsonSerializer.DeserializeAsync<PendingAnnotationManifest>(
                    manifestStream,
                    cancellationToken: cancellationToken)
                    ?? throw new InvalidDataException("The annotation manifest is empty.");
                if (manifest.AnnotationId == Guid.Empty)
                {
                    throw new InvalidDataException("The annotation manifest has no annotation id.");
                }

                pending.Add(new PendingAnnotationBundle(
                    path,
                    new AnnotationBundle(manifest.AnnotationId, manifest.CapturedAtUtc, bytes)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Logger.Warning($"Unable to read annotation outbox bundle '{path}': {exception.Message}");
            }
        }

        return pending;
    }

    private static string ResolveDirectory(string? configuredDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Path.GetTempPath();
        }

        return Path.Combine(appData, "Ansight", "annotations", "outbox");
    }

    private sealed class PendingAnnotationManifest
    {
        [JsonPropertyName("annotationId")]
        public Guid AnnotationId { get; set; }

        [JsonPropertyName("capturedAtUtc")]
        public DateTimeOffset CapturedAtUtc { get; set; }
    }
}

internal sealed record PendingAnnotationBundle(string Path, AnnotationBundle Bundle);
