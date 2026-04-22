namespace Ansight.Tools.FileSystem;

public sealed class PushFileTool : ITool
{
    private readonly FileSystemToolsOptions options;

    public PushFileTool(FileSystemToolsOptions? options = null)
    {
        this.options = options ?? FileSystemToolsOptions.Default;
    }

    public string Category => "files";

    public ToolScope Scope => ToolScope.Write;

    public string Id => FileSystemToolIds.PushFile;

    public string Name => "Push File";

    public string Description => "Writes caller-provided file content into a folder inside the app sandbox.";

    public string Keywords => "filesystem file upload push write sandbox base64 text";

    public ToolSchema ArgumentsSchema => FileSystemToolSchemas.PushFileArguments;

    public ToolSchema ResultSchema => FileSystemToolSchemas.PushFileResult;

    public ToolSecurity Security => FileSystemToolSecurityProfiles.PushFile;

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var roots = FileSystemSandbox.GetRoots(options);
            var resolvedDirectory = FileSystemWriteHelpers.ResolveDirectory(
                arguments,
                roots,
                pathKey: "directoryPath",
                rootKey: "root",
                requireExisting: false);
            var fileName = FileSystemWriteHelpers.GetSafeFileName(arguments);
            var destinationFile = new FileSystemSandbox.ResolvedPath(
                resolvedDirectory.RootAlias,
                resolvedDirectory.RootPath,
                Path.GetFullPath(Path.Combine(resolvedDirectory.FullPath, fileName)));
            var createDirectory = FileSystemSandbox.GetBoolean(arguments, "createDirectory", defaultValue: true);
            var overwrite = FileSystemSandbox.GetBoolean(arguments, "overwrite", defaultValue: false);
            var content = FileSystemWriteHelpers.GetPushContent(arguments);

            var overwritten = FileSystemWriteHelpers.EnsureDestinationFileCanBeWritten(destinationFile, overwrite);
            var createdDirectory = FileSystemWriteHelpers.EnsureParentDirectoryExists(destinationFile, createDirectory);

            var fileMode = overwrite ? FileMode.Create : FileMode.CreateNew;
            await using (var stream = new FileStream(destinationFile.FullPath, fileMode, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(content);
            }

            var payload = FileSystemWriteHelpers.CreateDestinationFilePayload(
                overwritten ? "overwritten" : "created",
                destinationFile,
                roots,
                overwritten,
                createdDirectory);

            return ToolResult.Success(payload);
        }
        catch (Exception exception)
        {
            return ToolResult.Failure(exception.Message, errorCode: "filesystem_push_failed");
        }
    }
}
