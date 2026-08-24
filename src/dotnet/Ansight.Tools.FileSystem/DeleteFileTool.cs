namespace Ansight.Tools.FileSystem;

public sealed class DeleteFileTool : ITool
{
    private readonly FileSystemToolsOptions options;

    public DeleteFileTool(FileSystemToolsOptions? options = null)
    {
        this.options = options ?? FileSystemToolsOptions.Default;
    }

    public string Category => "files";

    public ToolPolicy Policy => ToolPolicy.Critical;

    public string Id => FileSystemToolIds.DeleteFile;

    public string Name => "Delete File";

    public string Description => "Deletes a file from a sandbox-constrained path.";

    public string Keywords => "filesystem file delete remove sandbox";

    public ToolSchema ArgumentsSchema => FileSystemToolSchemas.DeleteFileArguments;

    public ToolSchema ResultSchema => FileSystemToolSchemas.DeleteFileResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var roots = FileSystemSandbox.GetRoots(options);
            var resolvedFile = FileSystemSandbox.ResolvePath(
                arguments,
                roots,
                requireExisting: true,
                expectDirectory: false);
            var fileInfo = new FileInfo(resolvedFile.FullPath);
            var payload = FileSystemContentDescriptor.CreateResolvedFilePayload(resolvedFile, roots, fileInfo);

            File.Delete(resolvedFile.FullPath);

            payload["deleted"] = true;
            payload["capturedAtUtc"] = DateTime.UtcNow.ToString("O");

            return Task.FromResult(ToolResult.Success(payload));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "filesystem_delete_failed"));
        }
    }
}
