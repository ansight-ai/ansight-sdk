namespace Ansight.Tools.FileSystem;

using System.Text;
using System.Text.Json.Nodes;

#if ANDROID
using Android.App;
#elif IOS || MACCATALYST
using Foundation;
#endif

internal static class FileSystemSandbox
{
    private static readonly EnumerationOptions SafeEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false
    };

    internal sealed record ResolvedPath(string RootAlias, string RootPath, string FullPath);

    internal static IReadOnlyDictionary<string, string> GetRoots(FileSystemToolsOptions? options)
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetPlatformRoots())
        {
            AddRoot(roots, root.Alias, root.Path);
        }

        if (options is not null)
        {
            foreach (var root in options.AdditionalRoots)
            {
                AddRoot(roots, root.Tag, root.Path);
            }
        }

        if (roots.Count == 0)
        {
            throw new InvalidOperationException("No sandbox roots are available for the current app.");
        }

        return roots;
    }

    internal static ResolvedPath ResolvePath(
        IReadOnlyDictionary<string, string> arguments,
        IReadOnlyDictionary<string, string> roots,
        bool requireExisting,
        bool expectDirectory)
    {
        var requestedPath = GetString(arguments, "path");
        var requestedRoot = GetString(arguments, "root");

        if (!string.IsNullOrWhiteSpace(requestedRoot))
        {
            var explicitRoot = ResolveRoot(roots, requestedRoot);
            var explicitFullPath = BuildFullPath(explicitRoot.RootPath, requestedPath);
            EnsurePathIsWithinRoot(explicitFullPath, explicitRoot.RootPath);
            EnsurePathExists(explicitFullPath, requireExisting, expectDirectory, requestedPath, searchAllRoots: false);
            return new ResolvedPath(explicitRoot.RootAlias, explicitRoot.RootPath, explicitFullPath);
        }

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            var defaultRoot = SelectDefaultRoot(roots);
            return new ResolvedPath(defaultRoot.RootAlias, defaultRoot.RootPath, defaultRoot.RootPath);
        }

        if (Path.IsPathRooted(requestedPath))
        {
            var absoluteFullPath = Path.GetFullPath(requestedPath);
            var matchingRoot = FindContainingRoot(roots, absoluteFullPath)
                ?? throw new InvalidOperationException($"The path '{absoluteFullPath}' is outside the approved app sandbox roots.");

            EnsurePathExists(absoluteFullPath, requireExisting, expectDirectory, requestedPath, searchAllRoots: false);
            return new ResolvedPath(matchingRoot.RootAlias, matchingRoot.RootPath, absoluteFullPath);
        }

        var relativeMatch = FindExistingRelativePathMatch(roots, requestedPath, expectDirectory);
        if (relativeMatch.HasValue)
        {
            return new ResolvedPath(
                relativeMatch.Value.RootAlias,
                relativeMatch.Value.RootPath,
                Path.GetFullPath(Path.Combine(relativeMatch.Value.RootPath, requestedPath)));
        }

        var fallbackRoot = SelectDefaultRoot(roots);
        var fallbackFullPath = BuildFullPath(fallbackRoot.RootPath, requestedPath);
        EnsurePathExists(fallbackFullPath, requireExisting, expectDirectory, requestedPath, searchAllRoots: true);
        return new ResolvedPath(fallbackRoot.RootAlias, fallbackRoot.RootPath, fallbackFullPath);
    }

    internal static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateDirectories("*", SafeEnumerationOptions).ToArray();
        }
        catch
        {
            return Array.Empty<DirectoryInfo>();
        }
    }

    internal static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SafeEnumerationOptions).ToArray();
        }
        catch
        {
            return Array.Empty<FileInfo>();
        }
    }

    internal static JsonArray DescribeRoots(IReadOnlyDictionary<string, string> roots)
    {
        var entries = new JsonArray();
        foreach (var root in roots)
        {
            entries.Add(new JsonObject
            {
                ["alias"] = root.Key,
                ["path"] = root.Value
            });
        }

        return entries;
    }

    internal static JsonObject DescribeEntry(FileSystemInfo entry, string rootAlias, string rootPath)
    {
        var isDirectory = entry.Attributes.HasFlag(FileAttributes.Directory);
        long? sizeBytes = null;
        string? fileExtension = null;
        string? mimeType = null;
        if (!isDirectory && entry is FileInfo fileInfo)
        {
            sizeBytes = fileInfo.Length;
            fileExtension = FileSystemContentDescriptor.GetFileExtension(fileInfo.Name);
            mimeType = FileSystemContentDescriptor.GetMimeType(fileInfo.Name);
        }

        return new JsonObject
        {
            ["name"] = entry.Name,
            ["path"] = entry.FullName,
            ["relativePath"] = Path.GetRelativePath(rootPath, entry.FullName),
            ["rootAlias"] = rootAlias,
            ["kind"] = isDirectory ? "directory" : "file",
            ["sizeBytes"] = sizeBytes,
            ["fileExtension"] = fileExtension,
            ["mimeType"] = mimeType,
            ["lastModifiedUtc"] = entry.LastWriteTimeUtc.ToString("O"),
            ["isHidden"] = IsHidden(entry)
        };
    }

    internal static bool IsHidden(FileSystemInfo info)
    {
        if (info.Name.StartsWith(".", StringComparison.Ordinal))
        {
            return true;
        }

        return info.Attributes.HasFlag(FileAttributes.Hidden);
    }

    internal static int GetInt(IReadOnlyDictionary<string, string> arguments, string key, int defaultValue, int minimum, int maximum)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            throw new InvalidOperationException($"The argument '{key}' must be an integer.");
        }

        return Math.Clamp(parsedValue, minimum, maximum);
    }

    internal static bool GetBoolean(IReadOnlyDictionary<string, string> arguments, string key, bool defaultValue)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (bool.TryParse(rawValue, out var boolValue))
        {
            return boolValue;
        }

        return rawValue switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidOperationException($"The argument '{key}' must be a boolean.")
        };
    }

    internal static long GetLong(IReadOnlyDictionary<string, string> arguments, string key, long defaultValue, long minimum, long maximum)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!long.TryParse(rawValue, out var parsedValue))
        {
            throw new InvalidOperationException($"The argument '{key}' must be an integer.");
        }

        return Math.Clamp(parsedValue, minimum, maximum);
    }

    internal static string? GetString(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    internal static bool IsUtf8(byte[] bytes)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddRoot(IDictionary<string, string> roots, string alias, string? path)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new InvalidOperationException("Sandbox root aliases must be non-empty.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            return;
        }

        if (roots.TryGetValue(alias, out var existingPath))
        {
            if (string.Equals(existingPath, fullPath, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"A sandbox root with alias '{alias}' is already registered for '{existingPath}'.");
        }

        if (roots.Values.Contains(fullPath, StringComparer.Ordinal))
        {
            return;
        }

        roots[alias] = fullPath;
    }

    private static string BuildFullPath(string rootPath, string? requestedPath)
    {
        var combinedPath = string.IsNullOrWhiteSpace(requestedPath)
            ? rootPath
            : Path.IsPathRooted(requestedPath)
                ? requestedPath
                : Path.Combine(rootPath, requestedPath);

        return Path.GetFullPath(combinedPath);
    }

    private static (string RootAlias, string RootPath) ResolveRoot(IReadOnlyDictionary<string, string> roots, string? requestedRoot)
    {
        if (string.IsNullOrWhiteSpace(requestedRoot))
        {
            throw new InvalidOperationException("The 'root' argument must be a non-empty sandbox root alias.");
        }

        if (roots.TryGetValue(requestedRoot, out var matchingRoot))
        {
            return (requestedRoot, matchingRoot);
        }

        var candidate = Path.GetFullPath(requestedRoot);
        var matchingAlias = roots.FirstOrDefault(root => string.Equals(root.Value, candidate, StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(matchingAlias.Key))
        {
            return (matchingAlias.Key, matchingAlias.Value);
        }

        throw new InvalidOperationException($"The sandbox root '{requestedRoot}' is not available.");
    }

    private static (string RootAlias, string RootPath)? FindContainingRoot(IReadOnlyDictionary<string, string> roots, string fullPath)
    {
        foreach (var root in roots)
        {
            if (IsWithinRoot(fullPath, root.Value))
            {
                return (root.Key, root.Value);
            }
        }

        return null;
    }

    private static (string RootAlias, string RootPath)? FindExistingRelativePathMatch(
        IReadOnlyDictionary<string, string> roots,
        string requestedPath,
        bool expectDirectory)
    {
        var matches = new List<(string RootAlias, string RootPath)>();
        foreach (var root in roots)
        {
            var candidatePath = Path.GetFullPath(Path.Combine(root.Value, requestedPath));
            if (!IsWithinRoot(candidatePath, root.Value))
            {
                continue;
            }

            if (PathExists(candidatePath, expectDirectory))
            {
                matches.Add((root.Key, root.Value));
            }
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"The relative path '{requestedPath}' exists in multiple approved sandbox roots. Specify the 'root' argument explicitly.")
        };
    }

    private static (string RootAlias, string RootPath) SelectDefaultRoot(IReadOnlyDictionary<string, string> roots)
    {
        if (roots.TryGetValue("appData", out var appDataPath))
        {
            return ("appData", appDataPath);
        }

        foreach (var root in roots)
        {
            if (RootHasEntries(root.Value))
            {
                return (root.Key, root.Value);
            }
        }

        var firstRoot = roots.First();
        return (firstRoot.Key, firstRoot.Value);
    }

    private static IEnumerable<(string Alias, string? Path)> GetPlatformRoots()
    {
#if ANDROID
        yield return ("appData", Application.Context?.FilesDir?.AbsolutePath);
        yield return ("cache", Application.Context?.CacheDir?.AbsolutePath);
#elif IOS || MACCATALYST
        yield return ("appData", GetAppleDirectory(NSSearchPathDirectory.LibraryDirectory));
        yield return ("documents", GetAppleDirectory(NSSearchPathDirectory.DocumentDirectory));
        yield return ("cache", GetAppleDirectory(NSSearchPathDirectory.CachesDirectory));
#else
        yield return ("appData", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        yield return ("documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        yield return ("cache", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
#endif
        yield return ("temp", Path.GetTempPath());
    }

#if IOS || MACCATALYST
    private static string? GetAppleDirectory(NSSearchPathDirectory directory)
    {
        var directories = NSSearchPath.GetDirectories(directory, NSSearchPathDomain.User);
        if (directories is null || directories.Length == 0)
        {
            return null;
        }

        return directories[0];
    }
#endif

    private static bool RootHasEntries(string rootPath)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(rootPath, "*", SafeEnumerationOptions).Any();
        }
        catch
        {
            return false;
        }
    }

    private static void EnsurePathIsWithinRoot(string fullPath, string rootPath)
    {
        if (!IsWithinRoot(fullPath, rootPath))
        {
            throw new InvalidOperationException($"The path '{fullPath}' is outside the approved app sandbox root '{rootPath}'.");
        }
    }

    private static void EnsurePathExists(
        string fullPath,
        bool requireExisting,
        bool expectDirectory,
        string? requestedPath,
        bool searchAllRoots)
    {
        if (!requireExisting)
        {
            return;
        }

        if (expectDirectory && Directory.Exists(fullPath))
        {
            return;
        }

        if (!expectDirectory && File.Exists(fullPath))
        {
            return;
        }

        if (searchAllRoots && !string.IsNullOrWhiteSpace(requestedPath))
        {
            if (expectDirectory)
            {
                throw new DirectoryNotFoundException(
                    $"The directory '{requestedPath}' was not found in any approved app sandbox root.");
            }

            throw new FileNotFoundException(
                $"The file '{requestedPath}' was not found in any approved app sandbox root.",
                requestedPath);
        }

        if (expectDirectory)
        {
            throw new DirectoryNotFoundException($"The directory '{fullPath}' does not exist.");
        }

        throw new FileNotFoundException($"The file '{fullPath}' does not exist.", fullPath);
    }

    private static bool PathExists(string fullPath, bool expectDirectory)
        => expectDirectory ? Directory.Exists(fullPath) : File.Exists(fullPath);

    private static bool IsWithinRoot(string path, string rootPath)
    {
        if (string.Equals(path, rootPath, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedRoot = rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }
}
