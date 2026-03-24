namespace Ansight.Tools.FileSystem;

using System.Text.Json.Nodes;

public sealed class ListDirectoryTool : ITool
{
    private const int DefaultMaxEntries = 200;
    private const int DefaultMaxDepth = 1;
    private readonly FileSystemToolsOptions options;

    public ListDirectoryTool(FileSystemToolsOptions? options = null)
    {
        this.options = options ?? FileSystemToolsOptions.Default;
    }

    public string Category => "files";

    public ToolScope Scope => ToolScope.Read;

    public string Id => FileSystemToolIds.ListDirectory;

    public string Name => "List Directory";

    public string Description => "Lists files and folders inside the app sandbox.";

    public string Keywords => "filesystem files directory sandbox";

    public ToolSchema ArgumentsSchema => FileSystemToolSchemas.ListDirectoryArguments;

    public ToolSchema ResultSchema => FileSystemToolSchemas.ListDirectoryResult;

    public ToolSecurity Security => FileSystemToolSecurityProfiles.ListDirectory;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var roots = FileSystemSandbox.GetRoots(options);
            var resolvedDirectory = FileSystemSandbox.ResolvePath(arguments, roots, requireExisting: true, expectDirectory: true);
            var includeHidden = FileSystemSandbox.GetBoolean(arguments, "includeHidden", defaultValue: false);
            var recursive = FileSystemSandbox.GetBoolean(arguments, "recursive", defaultValue: false);
            var maxDepth = recursive
                ? FileSystemSandbox.GetInt(arguments, "maxDepth", defaultValue: 5, minimum: 1, maximum: 16)
                : FileSystemSandbox.GetInt(arguments, "maxDepth", defaultValue: DefaultMaxDepth, minimum: 1, maximum: DefaultMaxDepth);
            var maxEntries = FileSystemSandbox.GetInt(arguments, "maxEntries", defaultValue: DefaultMaxEntries, minimum: 1, maximum: 1000);

            var entries = new JsonArray();
            var pending = new Queue<(DirectoryInfo directory, int depth)>();
            pending.Enqueue((new DirectoryInfo(resolvedDirectory.FullPath), 0));

            while (pending.Count > 0 && entries.Count < maxEntries)
            {
                var (directory, depth) = pending.Dequeue();

                foreach (var childDirectory in FileSystemSandbox.SafeEnumerateDirectories(directory))
                {
                    if (!includeHidden && FileSystemSandbox.IsHidden(childDirectory))
                    {
                        continue;
                    }

                    entries.Add(FileSystemSandbox.DescribeEntry(childDirectory, resolvedDirectory.RootAlias, resolvedDirectory.RootPath));

                    if (depth + 1 < maxDepth)
                    {
                        pending.Enqueue((childDirectory, depth + 1));
                    }

                    if (entries.Count >= maxEntries)
                    {
                        break;
                    }
                }

                foreach (var file in FileSystemSandbox.SafeEnumerateFiles(directory))
                {
                    if (!includeHidden && FileSystemSandbox.IsHidden(file))
                    {
                        continue;
                    }

                    entries.Add(FileSystemSandbox.DescribeEntry(file, resolvedDirectory.RootAlias, resolvedDirectory.RootPath));

                    if (entries.Count >= maxEntries)
                    {
                        break;
                    }
                }
            }

            var payload = new JsonObject
            {
                ["rootAlias"] = resolvedDirectory.RootAlias,
                ["rootPath"] = resolvedDirectory.RootPath,
                ["directoryPath"] = resolvedDirectory.FullPath,
                ["relativePath"] = Path.GetRelativePath(resolvedDirectory.RootPath, resolvedDirectory.FullPath),
                ["availableRoots"] = FileSystemSandbox.DescribeRoots(roots),
                ["entries"] = entries,
                ["truncated"] = entries.Count >= maxEntries,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
            };

            return Task.FromResult(ToolResult.Success(payload));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "filesystem_list_failed"));
        }
    }
}
