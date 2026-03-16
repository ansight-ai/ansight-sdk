namespace Ansight.Tools.FileSystem;

using System.Text;
using System.Text.Json.Nodes;

internal static class FileSystemSandbox
{
    internal sealed record ResolvedPath(string RootAlias, string RootPath, string FullPath);

    internal static IReadOnlyDictionary<string, string> GetRoots()
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddRoot(roots, "appData", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddRoot(roots, "documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddRoot(roots, "personal", Environment.GetFolderPath(Environment.SpecialFolder.Personal));
        AddRoot(roots, "temp", Path.GetTempPath());

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

        var (rootAlias, rootPath) = ResolveRoot(roots, requestedRoot);
        var combinedPath = string.IsNullOrWhiteSpace(requestedPath)
            ? rootPath
            : Path.IsPathRooted(requestedPath)
                ? requestedPath
                : Path.Combine(rootPath, requestedPath);

        var fullPath = Path.GetFullPath(combinedPath);

        if (!roots.Values.Any(root => IsWithinRoot(fullPath, root)))
        {
            throw new InvalidOperationException($"The path '{fullPath}' is outside the approved app sandbox roots.");
        }

        if (requireExisting)
        {
            if (expectDirectory && !Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"The directory '{fullPath}' does not exist.");
            }

            if (!expectDirectory && !File.Exists(fullPath))
            {
                throw new FileNotFoundException($"The file '{fullPath}' does not exist.", fullPath);
            }
        }

        return new ResolvedPath(rootAlias, rootPath, fullPath);
    }

    internal static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateDirectories();
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
            return directory.EnumerateFiles();
        }
        catch
        {
            return Array.Empty<FileInfo>();
        }
    }

    internal static JsonObject DescribeEntry(FileSystemInfo entry, string rootAlias, string rootPath)
    {
        var isDirectory = entry.Attributes.HasFlag(FileAttributes.Directory);
        long? sizeBytes = null;
        if (!isDirectory && entry is FileInfo fileInfo)
        {
            sizeBytes = fileInfo.Length;
        }

        return new JsonObject
        {
            ["name"] = entry.Name,
            ["path"] = entry.FullName,
            ["relativePath"] = Path.GetRelativePath(rootPath, entry.FullName),
            ["rootAlias"] = rootAlias,
            ["kind"] = isDirectory ? "directory" : "file",
            ["sizeBytes"] = sizeBytes,
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
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            return;
        }

        if (roots.Values.Contains(fullPath, StringComparer.Ordinal))
        {
            return;
        }

        roots[alias] = fullPath;
    }

    private static (string RootAlias, string RootPath) ResolveRoot(IReadOnlyDictionary<string, string> roots, string? requestedRoot)
    {
        if (!string.IsNullOrWhiteSpace(requestedRoot))
        {
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
        }

        var firstRoot = roots.First();
        return (firstRoot.Key, firstRoot.Value);
    }

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
