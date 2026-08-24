namespace Ansight.Tools.FileSystem;

public sealed class MoveFileTool : ITool
{
    private readonly FileSystemToolsOptions options;

    public MoveFileTool(FileSystemToolsOptions? options = null)
    {
        this.options = options ?? FileSystemToolsOptions.Default;
    }

    public string Category => "files";

    public ToolPolicy Policy => ToolPolicy.Write;

    public string Id => FileSystemToolIds.MoveFile;

    public string Name => "Move File";

    public string Description => "Moves or renames a file between sandbox-constrained paths.";

    public string Keywords => "filesystem file move rename sandbox";

    public ToolSchema ArgumentsSchema => FileSystemToolSchemas.MoveFileArguments;

    public ToolSchema ResultSchema => FileSystemToolSchemas.MoveFileResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var roots = FileSystemSandbox.GetRoots(options);
            var sourceFile = FileSystemWriteHelpers.ResolveFile(
                arguments,
                roots,
                pathKey: "sourcePath",
                rootKey: "root",
                requireExisting: true);
            var destinationFile = FileSystemWriteHelpers.ResolveDestinationFile(arguments, roots, sourceFile);
            FileSystemWriteHelpers.EnsureDifferentPaths(sourceFile, destinationFile);

            var overwrite = FileSystemSandbox.GetBoolean(arguments, "overwrite", defaultValue: false);
            var createDirectory = FileSystemSandbox.GetBoolean(arguments, "createDirectory", defaultValue: true);
            var overwritten = FileSystemWriteHelpers.EnsureDestinationFileCanBeWritten(destinationFile, overwrite);
            var createdDirectory = FileSystemWriteHelpers.EnsureParentDirectoryExists(destinationFile, createDirectory);

            File.Move(sourceFile.FullPath, destinationFile.FullPath, overwrite);

            var payload = FileSystemWriteHelpers.CreateFileTransferPayload(
                "moved",
                sourceFile,
                destinationFile,
                roots,
                overwritten,
                createdDirectory);

            return Task.FromResult(ToolResult.Success(payload));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "filesystem_move_failed"));
        }
    }
}
