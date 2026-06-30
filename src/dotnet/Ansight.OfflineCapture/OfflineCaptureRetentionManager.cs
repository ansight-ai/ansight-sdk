namespace Ansight.OfflineCapture;

internal static class OfflineCaptureRetentionManager
{
    public static Task ApplyAsync(
        string rootDirectory,
        string? activeSessionDirectory,
        OfflineCaptureEffectiveOptions options,
        IReadOnlyCollection<string?> activeFiles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(options);

        if (!Directory.Exists(rootDirectory))
        {
            return Task.CompletedTask;
        }

        var activeFileSet = activeFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cutoffUtc = DateTimeOffset.UtcNow - options.RetentionWindow;

        DeleteExpiredFiles(rootDirectory, activeFileSet, cutoffUtc, cancellationToken);

        if (!string.IsNullOrWhiteSpace(activeSessionDirectory) && Directory.Exists(activeSessionDirectory))
        {
            TrimDirectoryToSize(activeSessionDirectory, options.MaximumSessionBytes, activeFileSet, cancellationToken);
        }

        TrimDirectoryToSize(rootDirectory, options.MaximumRetainedBytes, activeFileSet, cancellationToken);
        DeleteEmptySessionDirectories(rootDirectory);
        return Task.CompletedTask;
    }

    private static void DeleteExpiredFiles(
        string rootDirectory,
        HashSet<string> activeFiles,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(file);
            if (activeFiles.Contains(fullPath))
            {
                continue;
            }

            if (Path.GetFileName(file).Equals(OfflineCapturePaths.SettingsFileName, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(file).Equals(OfflineCapturePaths.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.GetLastWriteTimeUtc(file) < cutoffUtc.UtcDateTime)
            {
                TryDeleteFile(file);
            }
        }
    }

    private static void TrimDirectoryToSize(
        string directoryPath,
        long maxBytes,
        HashSet<string> activeFiles,
        CancellationToken cancellationToken)
    {
        var files = Directory
            .EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .ToList();
        var totalBytes = files.Sum(file => file.Length);
        if (totalBytes <= maxBytes)
        {
            return;
        }

        foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (totalBytes <= maxBytes)
            {
                break;
            }

            var fullPath = Path.GetFullPath(file.FullName);
            if (activeFiles.Contains(fullPath))
            {
                continue;
            }

            if (file.Name.Equals(OfflineCapturePaths.SettingsFileName, StringComparison.OrdinalIgnoreCase) ||
                file.Name.Equals(OfflineCapturePaths.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var length = file.Length;
            if (TryDeleteFile(file.FullName))
            {
                totalBytes -= length;
            }
        }
    }

    private static void DeleteEmptySessionDirectories(string rootDirectory)
    {
        var sessionsDirectory = OfflineCapturePaths.SessionsDirectory(rootDirectory);
        if (!Directory.Exists(sessionsDirectory))
        {
            return;
        }

        foreach (var sessionDirectory in Directory.EnumerateDirectories(sessionsDirectory))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(sessionDirectory).Any())
                {
                    Directory.Delete(sessionDirectory, recursive: false);
                }
            }
            catch
            {
                // Cleanup is best-effort and must not interrupt capture.
            }
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
