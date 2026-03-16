namespace Ansight.Tools.FileSystem;

using System.Text;
using System.Text.Json.Nodes;

public sealed class ReadFileTool : ITool
{
    private const int DefaultMaxBytes = 64 * 1024;

    public string Category => "files";

    public ToolScope Scope => ToolScope.Read;

    public string Id => "files.read_file";

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
            var roots = FileSystemSandbox.GetRoots();
            var resolvedFile = FileSystemSandbox.ResolvePath(arguments, roots, requireExisting: true, expectDirectory: false);
            var maxBytes = FileSystemSandbox.GetInt(arguments, "maxBytes", defaultValue: DefaultMaxBytes, minimum: 1, maximum: 1024 * 1024);
            var encoding = FileSystemSandbox.GetString(arguments, "encoding");

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

            var fileInfo = new FileInfo(resolvedFile.FullPath);
            var truncated = fileInfo.Length > buffer.Length;
            var requestedEncoding = encoding?.Trim().ToLowerInvariant();
            var isText = requestedEncoding switch
            {
                "utf8" => true,
                "base64" => false,
                _ => FileSystemSandbox.IsUtf8(buffer)
            };

            var payload = new JsonObject
            {
                ["rootAlias"] = resolvedFile.RootAlias,
                ["rootPath"] = resolvedFile.RootPath,
                ["filePath"] = resolvedFile.FullPath,
                ["relativePath"] = Path.GetRelativePath(resolvedFile.RootPath, resolvedFile.FullPath),
                ["sizeBytes"] = fileInfo.Length,
                ["bytesRead"] = buffer.Length,
                ["truncated"] = truncated,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
            };

            if (isText)
            {
                payload["contentType"] = "text";
                payload["encoding"] = "utf-8";
                payload["text"] = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(buffer);
            }
            else
            {
                payload["contentType"] = "binary";
                payload["encoding"] = "base64";
                payload["base64"] = Convert.ToBase64String(buffer);
            }

            return ToolResult.Success(payload);
        }
        catch (Exception exception)
        {
            return ToolResult.Failure(exception.Message, errorCode: "filesystem_read_failed");
        }
    }
}
