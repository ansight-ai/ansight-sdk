namespace Ansight.Tools.Database;

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

#if ANDROID
using Android.App;
#elif IOS || MACCATALYST
using Foundation;
#endif

internal static class DatabaseSupport
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int SqliteOpenReadOnly = 0x0000_0001;

    internal static Func<IEnumerable<(string Alias, string? Path)>>? PlatformRootsOverride { get; set; }

    internal static JsonObject ListDatabases(bool includeSystemStores, int maxResults)
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

    internal static JsonObject DescribeSchema(string databasePath, string? tableFilter)
    {
        using var database = Open(databasePath);
        var tables = ExecuteReadOnly(database, """
            SELECT name, type, sql
            FROM sqlite_master
            WHERE type IN ('table', 'view')
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name
            """, maxRows: 512);

        var tableDefinitions = new JsonArray();
        foreach (var rowNode in tables.Rows)
        {
            if (rowNode is not JsonObject row)
            {
                continue;
            }

            var name = row["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(tableFilter) && !string.Equals(tableFilter, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var escapedName = EscapeSqlLiteral(name);
            var columns = ExecuteReadOnly(database, $"PRAGMA table_xinfo('{escapedName}')", maxRows: 512);
            var indexes = ExecuteReadOnly(database, $"PRAGMA index_list('{escapedName}')", maxRows: 512);

            tableDefinitions.Add(new JsonObject
            {
                ["name"] = name,
                ["type"] = row["type"]?.GetValue<string>(),
                ["sql"] = row["sql"]?.GetValue<string>(),
                ["columns"] = columns.Rows,
                ["indexes"] = indexes.Rows
            });
        }

        return new JsonObject
        {
            ["databasePath"] = databasePath,
            ["tables"] = tableDefinitions,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static JsonObject ExecuteQuery(string databasePath, string sql, int maxRows)
    {
        using var database = Open(databasePath);
        var result = ExecuteReadOnly(database, sql, maxRows);

        return new JsonObject
        {
            ["databasePath"] = databasePath,
            ["sql"] = sql,
            ["columns"] = result.Columns,
            ["rows"] = result.Rows,
            ["truncated"] = result.Truncated,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static string ResolveDatabasePath(IReadOnlyDictionary<string, string> arguments)
    {
        var explicitPath = GetString(arguments, "path") ?? GetString(arguments, "database");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return ResolveSandboxPath(explicitPath);
        }

        throw new InvalidOperationException("The database tools require a 'path' argument that points to a SQLite database inside the app sandbox.");
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

    internal static string GetRequiredString(IReadOnlyDictionary<string, string> arguments, string key)
    {
        return GetString(arguments, key) ?? throw new InvalidOperationException($"The argument '{key}' is required.");
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

    private static string ResolveSandboxPath(string requestedPath)
    {
        var roots = GetRoots();

        string fullPath;
        if (Path.IsPathRooted(requestedPath))
        {
            fullPath = Path.GetFullPath(requestedPath);
        }
        else
        {
            var matchingRoot = FindExistingRelativePathMatch(roots, requestedPath);
            if (matchingRoot.HasValue)
            {
                fullPath = Path.GetFullPath(Path.Combine(matchingRoot.Value.RootPath, requestedPath));
            }
            else
            {
                var defaultRoot = SelectDefaultRoot(roots);
                if (string.IsNullOrWhiteSpace(defaultRoot.RootPath))
                {
                    throw new InvalidOperationException("No sandbox root is available for database lookup.");
                }

                fullPath = Path.GetFullPath(Path.Combine(defaultRoot.RootPath, requestedPath));
            }
        }

        if (!roots.Values.Any(root => IsWithinRoot(fullPath, root)))
        {
            throw new InvalidOperationException($"The path '{fullPath}' is outside the approved app sandbox roots.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The database '{fullPath}' does not exist.", fullPath);
        }

        if (!LooksLikeSqliteDatabase(fullPath))
        {
            throw new InvalidOperationException($"The file '{fullPath}' is not recognized as a SQLite database.");
        }

        return fullPath;
    }

    private static (string RootAlias, string RootPath)? FindExistingRelativePathMatch(
        IReadOnlyDictionary<string, string> roots,
        string requestedPath)
    {
        var matches = new List<(string RootAlias, string RootPath)>();
        foreach (var root in roots)
        {
            var candidatePath = Path.GetFullPath(Path.Combine(root.Value, requestedPath));
            if (!IsWithinRoot(candidatePath, root.Value))
            {
                continue;
            }

            if (File.Exists(candidatePath))
            {
                matches.Add((root.Key, root.Value));
            }
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"The database path '{requestedPath}' exists in multiple approved sandbox roots. Use an absolute path instead.")
        };
    }

    private static (string RootAlias, string RootPath) SelectDefaultRoot(IReadOnlyDictionary<string, string> roots)
    {
        if (roots.TryGetValue("appData", out var appDataPath))
        {
            return ("appData", appDataPath);
        }

        var firstRoot = roots.FirstOrDefault();
        return (firstRoot.Key, firstRoot.Value);
    }

    private static bool LooksLikeSqliteDatabase(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
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

    private static bool IsWithinRoot(string path, string rootPath)
    {
        if (string.Equals(path, rootPath, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedRoot = rootPath.EndsWith(Path.DirectorySeparatorChar) ? rootPath : rootPath + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    private static SqliteDatabase Open(string databasePath)
    {
        var result = sqlite3_open_v2(databasePath, out var handle, SqliteOpenReadOnly, IntPtr.Zero);
        if (result != SqliteOk || handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Unable to open SQLite database '{databasePath}': {GetError(handle)}");
        }

        return new SqliteDatabase(handle);
    }

    private static QueryResult ExecuteReadOnly(SqliteDatabase database, string sql, int maxRows)
    {
        var statement = PrepareSingleStatement(database, sql);

        try
        {
            if (sqlite3_stmt_readonly(statement) == 0)
            {
                throw new InvalidOperationException("Only read-only SQLite statements are supported.");
            }

            var columnCount = sqlite3_column_count(statement);
            var columns = new JsonArray();
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                columns.Add(Marshal.PtrToStringUTF8(sqlite3_column_name(statement, columnIndex)));
            }

            var rows = new JsonArray();
            var truncated = false;
            while (true)
            {
                var stepResult = sqlite3_step(statement);
                if (stepResult == SqliteDone)
                {
                    break;
                }

                if (stepResult != SqliteRow)
                {
                    throw new InvalidOperationException($"SQLite query execution failed: {GetError(database.Handle)}");
                }

                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    break;
                }

                var rowObject = new JsonObject();
                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    var name = Marshal.PtrToStringUTF8(sqlite3_column_name(statement, columnIndex)) ?? $"column_{columnIndex}";
                    rowObject[name] = ReadColumnValue(statement, columnIndex);
                }

                rows.Add(rowObject);
            }

            return new QueryResult(columns, rows, truncated);
        }
        finally
        {
            sqlite3_finalize(statement);
        }
    }

    private static IntPtr PrepareSingleStatement(SqliteDatabase database, string sql)
    {
        var sqlBytes = Encoding.UTF8.GetBytes(sql + "\0");
        var sqlHandle = GCHandle.Alloc(sqlBytes, GCHandleType.Pinned);

        try
        {
            var sqlPointer = sqlHandle.AddrOfPinnedObject();
            var prepareResult = sqlite3_prepare_v2(database.Handle, sqlPointer, sqlBytes.Length, out var statement, out var tail);
            if (prepareResult != SqliteOk || statement == IntPtr.Zero)
            {
                if (statement != IntPtr.Zero)
                {
                    sqlite3_finalize(statement);
                }

                throw new InvalidOperationException($"Failed to prepare SQLite statement: {GetError(database.Handle)}");
            }

            if (HasRemainingSql(sqlBytes, sqlPointer, tail))
            {
                sqlite3_finalize(statement);
                throw new InvalidOperationException("Only a single read-only SQLite statement is supported.");
            }

            return statement;
        }
        finally
        {
            sqlHandle.Free();
        }
    }

    private static bool HasRemainingSql(byte[] sqlBytes, IntPtr sqlPointer, IntPtr tail)
    {
        if (tail == IntPtr.Zero)
        {
            return false;
        }

        var offset = checked((int)(tail.ToInt64() - sqlPointer.ToInt64()));
        if (offset < 0 || offset >= sqlBytes.Length - 1)
        {
            return false;
        }

        var remainingSql = Encoding.UTF8.GetString(sqlBytes, offset, (sqlBytes.Length - 1) - offset);
        return !string.IsNullOrWhiteSpace(remainingSql);
    }

    private static JsonNode? ReadColumnValue(IntPtr statement, int columnIndex)
    {
        return sqlite3_column_type(statement, columnIndex) switch
        {
            1 => JsonValue.Create(sqlite3_column_int64(statement, columnIndex)),
            2 => JsonValue.Create(sqlite3_column_double(statement, columnIndex)),
            3 => JsonValue.Create(Marshal.PtrToStringUTF8(sqlite3_column_text(statement, columnIndex))),
            4 => ReadBlob(statement, columnIndex),
            5 => null,
            _ => null
        };
    }

    private static JsonNode ReadBlob(IntPtr statement, int columnIndex)
    {
        var pointer = sqlite3_column_blob(statement, columnIndex);
        var length = sqlite3_column_bytes(statement, columnIndex);
        if (pointer == IntPtr.Zero || length <= 0)
        {
            return JsonValue.Create(string.Empty);
        }

        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return JsonValue.Create(Convert.ToBase64String(bytes));
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string GetError(IntPtr databaseHandle)
    {
        if (databaseHandle == IntPtr.Zero)
        {
            return "unknown SQLite error";
        }

        return Marshal.PtrToStringUTF8(sqlite3_errmsg(databaseHandle)) ?? "unknown SQLite error";
    }

    [DllImport("sqlite3", EntryPoint = "sqlite3_open_v2", CharSet = CharSet.Ansi)]
    private static extern int sqlite3_open_v2(string filename, out IntPtr db, int flags, IntPtr zvfs);

    [DllImport("sqlite3", EntryPoint = "sqlite3_close_v2")]
    private static extern int sqlite3_close_v2(IntPtr db);

    [DllImport("sqlite3", EntryPoint = "sqlite3_prepare_v2")]
    private static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int numBytes, out IntPtr statement, out IntPtr tail);

    [DllImport("sqlite3", EntryPoint = "sqlite3_step")]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("sqlite3", EntryPoint = "sqlite3_finalize")]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("sqlite3", EntryPoint = "sqlite3_errmsg")]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport("sqlite3", EntryPoint = "sqlite3_stmt_readonly")]
    private static extern int sqlite3_stmt_readonly(IntPtr statement);

    [DllImport("sqlite3", EntryPoint = "sqlite3_column_count")]
    private static extern int sqlite3_column_count(IntPtr statement);

    [DllImport("sqlite3", EntryPoint = "sqlite3_column_name")]
    private static extern IntPtr sqlite3_column_name(IntPtr statement, int index);

    [DllImport("sqlite3", EntryPoint = "sqlite3_column_type")]
    private static extern int sqlite3_column_type(IntPtr statement, int index);

    [DllImport("sqlite3", EntryPoint = "sqlite3_column_int64")]
    private static extern long sqlite3_column_int64(IntPtr statement, int index);

    [DllImport("sqlite3", EntryPoint = "sqlite3_column_double")]
    private static extern double sqlite3_column_double(IntPtr statement, int index);

    [DllImport("sqlite3", EntryPoint = "sqlite3_column_text")]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);

    [DllImport("sqlite3", EntryPoint = "sqlite3_column_blob")]
    private static extern IntPtr sqlite3_column_blob(IntPtr statement, int index);

    [DllImport("sqlite3", EntryPoint = "sqlite3_column_bytes")]
    private static extern int sqlite3_column_bytes(IntPtr statement, int index);

    private sealed record QueryResult(JsonArray Columns, JsonArray Rows, bool Truncated);

    private sealed class SqliteDatabase : IDisposable
    {
        internal SqliteDatabase(IntPtr handle)
        {
            Handle = handle;
        }

        internal IntPtr Handle { get; }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                sqlite3_close_v2(Handle);
            }
        }
    }
}
