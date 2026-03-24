namespace Ansight.Tools.FileSystem;

using System.Globalization;
using System.Text.Json.Nodes;

public sealed class DownloadFileTool : ITool
{
    private const int DefaultMaxBytes = 256 * 1024;
    private const int AbsoluteMaxBytes = 1024 * 1024;
    private readonly FileSystemToolsOptions options;

    public DownloadFileTool(FileSystemToolsOptions? options = null)
    {
        this.options = options ?? FileSystemToolsOptions.Default;
    }

    public string Category => "files";

    public ToolScope Scope => ToolScope.Read;

    public string Id => FileSystemToolIds.DownloadFile;

    public string Name => "Download File";

    public string Description => "Downloads a sandboxed file in resumable chunks with text or base64 payloads.";

    public string Keywords => "filesystem file download stream sandbox base64 text binary";

    public ToolSchema ArgumentsSchema => FileSystemToolSchemas.DownloadFileArguments;

    public ToolSchema ResultSchema => FileSystemToolSchemas.DownloadFileResult;

    public ToolSecurity Security => FileSystemToolSecurityProfiles.DownloadFile;

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var roots = FileSystemSandbox.GetRoots(options);
            var resolvedFile = FileSystemSandbox.ResolvePath(arguments, roots, requireExisting: true, expectDirectory: false);
            var fileInfo = new FileInfo(resolvedFile.FullPath);
            var version = FileSystemContentDescriptor.CreateVersion(fileInfo);
            var expectedVersion = FileSystemSandbox.GetString(arguments, "expectedVersion");
            if (!string.IsNullOrWhiteSpace(expectedVersion) &&
                !string.Equals(expectedVersion, version, StringComparison.Ordinal))
            {
                return ToolResult.Failure(
                    $"The file version changed from '{expectedVersion}' to '{version}'. Restart the download from offset 0.",
                    errorCode: "filesystem_download_version_mismatch");
            }

            var offsetBytes = FileSystemSandbox.GetLong(arguments, "offsetBytes", defaultValue: 0, minimum: 0, maximum: long.MaxValue);
            if (offsetBytes > fileInfo.Length)
            {
                return ToolResult.Failure(
                    $"The offset '{offsetBytes}' is beyond the end of the file ({fileInfo.Length} bytes).",
                    errorCode: "filesystem_download_offset_invalid");
            }

            var maxBytes = FileSystemSandbox.GetInt(arguments, "maxBytes", defaultValue: DefaultMaxBytes, minimum: 1, maximum: AbsoluteMaxBytes);
            var requestedEncoding = FileSystemSandbox.GetString(arguments, "encoding");

            var bytesToRead = (int)Math.Min(fileInfo.Length - offsetBytes, maxBytes);
            var buffer = new byte[bytesToRead];

            await using (var stream = new FileStream(resolvedFile.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                _ = stream.Seek(offsetBytes, SeekOrigin.Begin);

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

            var payload = FileSystemContentDescriptor.CreateResolvedFilePayload(resolvedFile, roots, fileInfo);
            var encodedContent = FileSystemContentDescriptor.EncodeContent(
                buffer,
                payload["mimeType"]!.GetValue<string>(),
                requestedEncoding);
            var nextOffsetBytes = offsetBytes + buffer.Length;
            var hasMore = nextOffsetBytes < fileInfo.Length;

            payload["offsetBytes"] = offsetBytes;
            payload["requestedMaxBytes"] = maxBytes;
            payload["bytesRead"] = buffer.Length;
            payload["hasMore"] = hasMore;
            payload["nextOffsetBytes"] = hasMore ? nextOffsetBytes : null;
            payload["capturedAtUtc"] = DateTime.UtcNow.ToString("O");
            payload["contentType"] = encodedContent.ContentType;
            payload["encoding"] = encodedContent.Encoding;
            payload["text"] = encodedContent.Text;
            payload["base64"] = encodedContent.Base64;
            payload["nextRequest"] = hasMore
                ? CreateNextRequest(resolvedFile, nextOffsetBytes, maxBytes, encodedContent.RequestedEncoding, version)
                : null;

            return ToolResult.Success(payload);
        }
        catch (Exception exception)
        {
            return ToolResult.Failure(exception.Message, errorCode: "filesystem_download_failed");
        }
    }

    private JsonObject CreateNextRequest(
        FileSystemSandbox.ResolvedPath resolvedFile,
        long nextOffsetBytes,
        int maxBytes,
        string requestedEncoding,
        string version)
    {
        return new JsonObject
        {
            ["toolId"] = Id,
            ["arguments"] = new JsonObject
            {
                ["root"] = resolvedFile.RootAlias,
                ["path"] = Path.GetRelativePath(resolvedFile.RootPath, resolvedFile.FullPath),
                ["offsetBytes"] = nextOffsetBytes.ToString(CultureInfo.InvariantCulture),
                ["maxBytes"] = maxBytes.ToString(CultureInfo.InvariantCulture),
                ["encoding"] = requestedEncoding,
                ["expectedVersion"] = version
            }
        };
    }
}
