namespace Ansight.Tools.Database;

using System.Text.Json.Nodes;

#if ANDROID
using Android.App;
#elif IOS || MACCATALYST
using Foundation;
#endif

public sealed class ListDatabasesTool : ITool
{
    private const int DefaultMaxResults = 200;

    internal static Func<IEnumerable<(string Alias, string? Path)>>? PlatformRootsOverride { get; set; }

    public string Category => "data";

    public ToolScope Scope => ToolScope.Read;

    public string Id => DatabaseToolIds.ListDatabases;

    public string Name => "List Databases";

    public string Description => "Lists the known app databases that can be inspected.";

    public string Keywords => "database sqlite storage schema";

    public ToolSchema ArgumentsSchema => DatabaseToolSchemas.ListDatabasesArguments;

    public ToolSchema ResultSchema => DatabaseToolSchemas.ListDatabasesResult;

    public ToolSecurity Security => DatabaseToolSecurityProfiles.ListDatabases;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var includeSystemStores = GetBoolean(arguments, "includeSystemStores", defaultValue: false);
            var maxResults = GetInt(arguments, "maxResults", defaultValue: DefaultMaxResults, minimum: 1, maximum: 1000);
            return Task.FromResult(ToolResult.Success(ListDatabases(includeSystemStores, maxResults)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "database_list_failed"));
        }
    }

    private static JsonObject ListDatabases(bool includeSystemStores, int maxResults)
    {
        var roots = GetRoots();
        var databases = new JsonArray();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in roots)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 8
            };

            foreach (var filePath in Directory.EnumerateFiles(root.Value, "*", options))
            {
                if (!includeSystemStores && IsSystemStorePath(filePath))
                {
                    continue;
                }

                if (!seenPaths.Add(filePath))
                {
                    continue;
                }

                if (!LooksLikeSqliteDatabase(filePath))
                {
                    continue;
                }

                var fileInfo = new FileInfo(filePath);
                databases.Add(new JsonObject
                {
                    ["name"] = fileInfo.Name,
                    ["path"] = fileInfo.FullName,
                    ["relativePath"] = Path.GetRelativePath(root.Value, fileInfo.FullName),
                    ["rootAlias"] = root.Key,
                    ["sizeBytes"] = fileInfo.Length,
                    ["lastModifiedUtc"] = fileInfo.LastWriteTimeUtc.ToString("O")
                });

                if (databases.Count >= maxResults)
                {
                    return new JsonObject
                    {
                        ["databases"] = databases,
                        ["truncated"] = true,
                        ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
                    };
                }
            }
        }

        return new JsonObject
        {
            ["databases"] = databases,
            ["truncated"] = false,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    private static int GetInt(IReadOnlyDictionary<string, string> arguments, string key, int defaultValue, int minimum, int maximum)
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

    private static bool GetBoolean(IReadOnlyDictionary<string, string> arguments, string key, bool defaultValue)
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

    private static IReadOnlyDictionary<string, string> GetRoots()
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in PlatformRootsOverride?.Invoke() ?? GetPlatformRoots())
        {
            AddRoot(roots, root.Alias, root.Path);
        }

        return roots;
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

        roots.Add(alias, fullPath);
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

    private static bool LooksLikeSqliteDatabase(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[16];
            if (stream.Read(header, 0, header.Length) != header.Length)
            {
                return false;
            }

            return System.Text.Encoding.ASCII.GetString(header).StartsWith("SQLite format 3", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSystemStorePath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}Caches{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}
