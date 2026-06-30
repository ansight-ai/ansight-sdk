using ICSharpCode.SharpZipLib.Zip;

namespace Ansight.OfflineCapture;

internal static class OfflineCaptureZipExporter
{
    public static async Task ExportAsync(
        string sourceDirectory,
        Stream destination,
        OfflineCaptureExportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(sourceDirectory);
        }

        if (options.UsePassword)
        {
            await ExportEncryptedAsync(sourceDirectory, destination, options, cancellationToken);
        }
        else
        {
            await ExportPlainAsync(sourceDirectory, destination, options, cancellationToken);
        }
    }

    private static async Task ExportPlainAsync(
        string sourceDirectory,
        Stream destination,
        OfflineCaptureExportOptions options,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var writer = new ZipArchiveEntryWriter(archive);
        await WriteArchiveEntriesAsync(sourceDirectory, writer, options, cancellationToken);
    }

    private static async Task ExportEncryptedAsync(
        string sourceDirectory,
        Stream destination,
        OfflineCaptureExportOptions options,
        CancellationToken cancellationToken)
    {
        using var zipStream = new ZipOutputStream(destination)
        {
            IsStreamOwner = false,
            Password = options.Password
        };
        zipStream.SetLevel(1);

        var writer = new SharpZipArchiveEntryWriter(zipStream);
        await WriteArchiveEntriesAsync(sourceDirectory, writer, options, cancellationToken);

        zipStream.Finish();
    }

    private static async Task WriteArchiveEntriesAsync(
        string sourceDirectory,
        IOfflineCaptureArchiveEntryWriter writer,
        OfflineCaptureExportOptions options,
        CancellationToken cancellationToken)
    {
        if (options.IncludeStudioSessionArchive)
        {
            await OfflineCaptureStudioArchiveWriter.WriteAsync(
                sourceDirectory,
                writer,
                cancellationToken);
        }

        if (!options.IncludeRawCaptureFiles)
        {
            return;
        }

        foreach (var filePath in EnumerateFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(filePath);
            var entryName = GetEntryName(sourceDirectory, filePath, options.IncludeRootDirectory);
            await writer.WriteEntryAsync(
                entryName,
                fileInfo.LastWriteTimeUtc,
                async (entryStream, token) =>
                {
                    await using var fileStream = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await fileStream.CopyToAsync(entryStream, token);
                },
                cancellationToken);
        }
    }

    private sealed class ZipArchiveEntryWriter(ZipArchive archive) : IOfflineCaptureArchiveEntryWriter
    {
        public async Task WriteEntryAsync(
            string entryName,
            DateTimeOffset lastWriteTimeUtc,
            Func<Stream, CancellationToken, Task> writeAsync,
            CancellationToken cancellationToken)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            entry.LastWriteTime = lastWriteTimeUtc;
            await using var entryStream = entry.Open();
            await writeAsync(entryStream, cancellationToken);
        }
    }

    private sealed class SharpZipArchiveEntryWriter(ZipOutputStream zipStream) : IOfflineCaptureArchiveEntryWriter
    {
        public async Task WriteEntryAsync(
            string entryName,
            DateTimeOffset lastWriteTimeUtc,
            Func<Stream, CancellationToken, Task> writeAsync,
            CancellationToken cancellationToken)
        {
            var entry = new ZipEntry(entryName)
            {
                DateTime = lastWriteTimeUtc.LocalDateTime,
                AESKeySize = 256
            };
            zipStream.PutNextEntry(entry);
            await writeAsync(zipStream, cancellationToken);
            zipStream.CloseEntry();
        }
    }

    private static IEnumerable<string> EnumerateFiles(string sourceDirectory)
    {
        return Directory
            .EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetEntryName(string sourceDirectory, string filePath, bool includeRootDirectory)
    {
        var relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
        if (!includeRootDirectory)
        {
            return relativePath;
        }

        var sessionId = Path.GetFileName(sourceDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        return $".ansight/sessions/{sessionId}/{relativePath}";
    }
}
