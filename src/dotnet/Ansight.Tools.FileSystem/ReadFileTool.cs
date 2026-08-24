namespace Ansight.Tools.FileSystem;

using System.Text.Json.Nodes;

public sealed class ReadFileTool : ITool
{
    private const int DefaultMaxBytes = 64 * 1024;
    private readonly FileSystemToolsOptions options;

    public ReadFileTool(FileSystemToolsOptions? options = null)
    {
        this.options = options ?? FileSystemToolsOptions.Default;
    }

    public string Category => "files";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => FileSystemToolIds.ReadFile;

    public string Name => "Read File";

    public string Description => "Reads a file from the app sandbox using a constrained path.";

    public string Keywords => "filesystem file read sandbox";

    public ToolSchema ArgumentsSchema => FileSystemToolSchemas.ReadFileArguments;

    public ToolSchema ResultSchema => FileSystemToolSchemas.ReadFileResult;

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var roots = FileSystemSandbox.GetRoots(options);
            var resolvedFile = FileSystemSandbox.ResolvePath(arguments, roots, requireExisting: true, expectDirectory: false);
            var maxBytes = FileSystemSandbox.GetInt(arguments, "maxBytes", defaultValue: DefaultMaxBytes, minimum: 1, maximum: 1024 * 1024);
            var encoding = FileSystemSandbox.GetString(arguments, "encoding");
            var fileInfo = new FileInfo(resolvedFile.FullPath);

            byte[] buffer;
            await using (var stream = new FileStream(resolvedFile.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bytesToRead = (int)Math.Min(stream.Length, maxBytes);
                buffer = new byte[bytesToRead];
                var totalRead = 0;
                while (totalRead < bytesToRead)
                {
                    var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, bytesToRead - totalRead));
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    totalRead += bytesRead;
                }

                if (totalRead != buffer.Length)
                {
                    Array.Resize(ref buffer, totalRead);
                }
            }

            var truncated = fileInfo.Length > buffer.Length;
            var payload = FileSystemContentDescriptor.CreateResolvedFilePayload(resolvedFile, roots, fileInfo);
            payload["bytesRead"] = buffer.Length;
            payload["truncated"] = truncated;
            payload["capturedAtUtc"] = DateTime.UtcNow.ToString("O");

            var encodedContent = FileSystemContentDescriptor.EncodeContent(
                buffer,
                payload["mimeType"]!.GetValue<string>(),
                encoding);

            payload["contentType"] = encodedContent.ContentType;
            payload["encoding"] = encodedContent.Encoding;
            payload["text"] = encodedContent.Text;
            payload["base64"] = encodedContent.Base64;

            return ToolResult.Success(payload);
        }
        catch (Exception exception)
        {
            return ToolResult.Failure(exception.Message, errorCode: "filesystem_read_failed");
        }
    }
}
