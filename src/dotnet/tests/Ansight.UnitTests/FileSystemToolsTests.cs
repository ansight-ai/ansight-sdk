using Ansight.Tools.FileSystem;
using System.Text;
using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

public sealed class FileSystemToolsTests
{
    [Fact]
    public void WithFileSystemTools_RegistersExpectedTools()
    {
        var options = Options.CreateBuilder()
            .WithFileSystemTools()
            .Build();

        Assert.Equal(
            [
                FileSystemToolIds.ListDirectory,
                FileSystemToolIds.ReadFile,
                FileSystemToolIds.GetFileChecksum,
                FileSystemToolIds.DownloadFile,
                FileSystemToolIds.BeginBinaryDownload,
                FileSystemToolIds.PushFile,
                FileSystemToolIds.CopyFile,
                FileSystemToolIds.MoveFile,
                FileSystemToolIds.DeleteFile
            ],
            options.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public async Task ListDirectoryTool_Execute_ListsRecursiveEntriesForConfiguredRoot()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.CreateDirectory(Path.Combine("content", "nested"));
        _ = tempDirectory.WriteTextFile(Path.Combine("content", "readme.txt"), "hello");
        _ = tempDirectory.WriteTextFile(Path.Combine("content", ".secret.txt"), "hidden");
        _ = tempDirectory.WriteTextFile(Path.Combine("content", "nested", "notes.txt"), "nested");

        var tool = new ListDirectoryTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "content",
            ["recursive"] = "true",
            ["maxDepth"] = "2",
            ["maxEntries"] = "10"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal("workspace", payload["rootAlias"]?.GetValue<string>());
        Assert.Equal(Path.Combine(tempDirectory.RootPath, "content"), payload["directoryPath"]?.GetValue<string>());

        var entries = Assert.IsType<JsonArray>(payload["entries"]);
        var relativePaths = entries
            .Select(node => Assert.IsType<JsonObject>(node)["relativePath"]?.GetValue<string>())
            .ToArray();
        var readmeEntry = entries
            .Select(node => Assert.IsType<JsonObject>(node))
            .Single(entry => entry["name"]?.GetValue<string>() == "readme.txt");

        Assert.Contains(Path.Combine("content", "readme.txt"), relativePaths);
        Assert.Contains(Path.Combine("content", "nested"), relativePaths);
        Assert.Contains(Path.Combine("content", "nested", "notes.txt"), relativePaths);
        Assert.DoesNotContain(Path.Combine("content", ".secret.txt"), relativePaths);
        Assert.Equal(".txt", readmeEntry["fileExtension"]?.GetValue<string>());
        Assert.Equal("text/plain", readmeEntry["mimeType"]?.GetValue<string>());
        Assert.False(payload["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ListDirectoryTool_Execute_RejectsPathTraversalOutsideConfiguredRoot()
    {
        using var tempDirectory = new TemporaryDirectory();
        var tool = new ListDirectoryTool(CreateOptions(tempDirectory.RootPath));

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = ".."
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("filesystem_list_failed", result.ErrorCode);
        Assert.Contains("outside the approved app sandbox root", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadFileTool_Execute_ReadsTextAndReportsTruncation()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.WriteTextFile("notes.txt", "hello world");

        var tool = new ReadFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "notes.txt",
            ["maxBytes"] = "5"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal("text", payload["contentType"]?.GetValue<string>());
        Assert.Equal("utf-8", payload["encoding"]?.GetValue<string>());
        Assert.Equal("notes.txt", payload["fileName"]?.GetValue<string>());
        Assert.Equal(".txt", payload["fileExtension"]?.GetValue<string>());
        Assert.Equal("text/plain", payload["mimeType"]?.GetValue<string>());
        Assert.Equal("hello", payload["text"]?.GetValue<string>());
        Assert.True(payload["truncated"]!.GetValue<bool>());
        Assert.Equal(5, payload["bytesRead"]!.GetValue<int>());
        Assert.NotNull(payload["version"]?.GetValue<string>());
    }

    [Fact]
    public async Task ReadFileTool_Execute_ReturnsBase64ForBinaryPayloads()
    {
        using var tempDirectory = new TemporaryDirectory();
        var bytes = new byte[] { 0xFF, 0x00, 0x80, 0x41 };
        _ = tempDirectory.WriteBinaryFile("payload.bin", bytes);

        var tool = new ReadFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "payload.bin",
            ["encoding"] = "base64"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal("binary", payload["contentType"]?.GetValue<string>());
        Assert.Equal("base64", payload["encoding"]?.GetValue<string>());
        Assert.Equal(".bin", payload["fileExtension"]?.GetValue<string>());
        Assert.Equal("application/octet-stream", payload["mimeType"]?.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(bytes), payload["base64"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetFileChecksumTool_Execute_ReturnsRequestedChecksumAlgorithms()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.WriteTextFile("notes.txt", "hello world");

        var tool = new GetFileChecksumTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "notes.txt",
            ["algorithms"] = "md5, sha1, sha-256, sha384, sha512, crc32"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal("notes.txt", payload["fileName"]?.GetValue<string>());
        Assert.Equal(".txt", payload["fileExtension"]?.GetValue<string>());
        Assert.Equal("text/plain", payload["mimeType"]?.GetValue<string>());
        Assert.Equal(11L, payload["sizeBytes"]?.GetValue<long>());

        var checksums = Assert.IsType<JsonArray>(payload["checksums"])
            .Select(node => Assert.IsType<JsonObject>(node))
            .ToDictionary(
                checksum => checksum["algorithm"]!.GetValue<string>(),
                checksum => checksum["checksum"]!.GetValue<string>());

        Assert.Equal("5eb63bbbe01eeed093cb22bb8f5acdc3", checksums["md5"]);
        Assert.Equal("2aae6c35c94fcfb415dbe95f408b9ce91ee846ed", checksums["sha1"]);
        Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", checksums["sha256"]);
        Assert.Equal("fdbd8e75a67f29f701a4e040385e2e23986303ea10239211af907fcbb83578b3e417cb71ce646efd0819dd8c088de1bd", checksums["sha384"]);
        Assert.Equal("309ecc489c12d6eb4cc40f50c902f2b4d0ed77ee511a7c7a9bcd3ca86d4cd86f989dd35bc5ff499670da34255b45b0cfd830e81f605dcf7dc5542e93ae9cd76f", checksums["sha512"]);
        Assert.Equal("0d4a1185", checksums["crc32"]);
    }

    [Fact]
    public async Task GetFileChecksumTool_Execute_DefaultsToSha256()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.WriteTextFile("notes.txt", "hello world");

        var tool = new GetFileChecksumTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "notes.txt"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        var checksum = Assert.Single(Assert.IsType<JsonArray>(payload["checksums"]));
        var checksumObject = Assert.IsType<JsonObject>(checksum);

        Assert.Equal("sha256", checksumObject["algorithm"]?.GetValue<string>());
        Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", checksumObject["checksum"]?.GetValue<string>());
        Assert.Equal("hex", checksumObject["encoding"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetFileChecksumTool_Execute_RejectsUnsupportedAlgorithms()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.WriteTextFile("notes.txt", "hello world");

        var tool = new GetFileChecksumTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "notes.txt",
            ["algorithms"] = "sha999"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("filesystem_checksum_failed", result.ErrorCode);
        Assert.Contains("Unsupported checksum algorithm", result.Message);
    }

    [Fact]
    public async Task DownloadFileTool_Execute_ReturnsChunkMetadataAndContinuation()
    {
        using var tempDirectory = new TemporaryDirectory();
        var bytes = Enumerable.Range(0, 10).Select(value => (byte)value).ToArray();
        _ = tempDirectory.WriteBinaryFile("payload.bin", bytes);

        var tool = new DownloadFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "payload.bin",
            ["offsetBytes"] = "2",
            ["maxBytes"] = "3",
            ["encoding"] = "base64"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal("payload.bin", payload["fileName"]?.GetValue<string>());
        Assert.Equal(".bin", payload["fileExtension"]?.GetValue<string>());
        Assert.Equal("application/octet-stream", payload["mimeType"]?.GetValue<string>());
        Assert.Equal(10L, payload["sizeBytes"]?.GetValue<long>());
        Assert.Equal(2L, payload["offsetBytes"]?.GetValue<long>());
        Assert.Equal(3, payload["requestedMaxBytes"]?.GetValue<int>());
        Assert.Equal(3, payload["bytesRead"]?.GetValue<int>());
        Assert.True(payload["hasMore"]!.GetValue<bool>());
        Assert.Equal(5L, payload["nextOffsetBytes"]?.GetValue<long>());
        Assert.Equal("binary", payload["contentType"]?.GetValue<string>());
        Assert.Equal("base64", payload["encoding"]?.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(bytes.Skip(2).Take(3).ToArray()), payload["base64"]?.GetValue<string>());

        var version = payload["version"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(version));

        var nextRequest = Assert.IsType<JsonObject>(payload["nextRequest"]);
        Assert.Equal(FileSystemToolIds.DownloadFile, nextRequest["toolId"]?.GetValue<string>());

        var nextArguments = Assert.IsType<JsonObject>(nextRequest["arguments"]);
        Assert.Equal("workspace", nextArguments["root"]?.GetValue<string>());
        Assert.Equal("payload.bin", nextArguments["path"]?.GetValue<string>());
        Assert.Equal("5", nextArguments["offsetBytes"]?.GetValue<string>());
        Assert.Equal("3", nextArguments["maxBytes"]?.GetValue<string>());
        Assert.Equal("base64", nextArguments["encoding"]?.GetValue<string>());
        Assert.Equal(version, nextArguments["expectedVersion"]?.GetValue<string>());
    }

    [Fact]
    public async Task DownloadFileTool_Execute_AutoDetectsTextPayloads()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.WriteTextFile("notes.md", "hello world");

        var tool = new DownloadFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "notes.md",
            ["offsetBytes"] = "0",
            ["maxBytes"] = "5"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal("text", payload["contentType"]?.GetValue<string>());
        Assert.Equal("utf-8", payload["encoding"]?.GetValue<string>());
        Assert.Equal("text/markdown", payload["mimeType"]?.GetValue<string>());
        Assert.Equal("hello", payload["text"]?.GetValue<string>());
    }

    [Fact]
    public async Task DownloadFileTool_Execute_RejectsVersionMismatch()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.WriteTextFile("notes.txt", "hello");

        var tool = new DownloadFileTool(CreateOptions(tempDirectory.RootPath));
        var initialResult = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "notes.txt",
            ["maxBytes"] = "2"
        });

        Assert.True(initialResult.IsSuccess);
        var initialPayload = Assert.IsType<JsonObject>(initialResult.Payload);
        var version = initialPayload["version"]!.GetValue<string>();

        _ = tempDirectory.WriteTextFile("notes.txt", "hello world");

        var resumedResult = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "notes.txt",
            ["offsetBytes"] = "2",
            ["maxBytes"] = "2",
            ["expectedVersion"] = version
        });

        Assert.False(resumedResult.IsSuccess);
        Assert.Equal("filesystem_download_version_mismatch", resumedResult.ErrorCode);
    }

    [Fact]
    public async Task BeginBinaryDownloadTool_Execute_RequiresActiveRuntimeAndPairingSession()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.WriteBinaryFile("payload.bin", [1, 2, 3, 4]);

        var tool = new BeginBinaryDownloadTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "payload.bin"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("filesystem_binary_download_unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task PushFileTool_Execute_WritesBase64ContentIntoSandboxFolder()
    {
        using var tempDirectory = new TemporaryDirectory();
        var bytes = new byte[] { 0x01, 0x02, 0xFF };

        var tool = new PushFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["directoryPath"] = Path.Combine("content", "incoming"),
            ["fileName"] = "payload.bin",
            ["contentBase64"] = Convert.ToBase64String(bytes)
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        var expectedPath = Path.Combine(tempDirectory.RootPath, "content", "incoming", "payload.bin");

        Assert.Equal(expectedPath, payload["filePath"]?.GetValue<string>());
        Assert.Equal(Path.Combine("content", "incoming", "payload.bin"), payload["relativePath"]?.GetValue<string>());
        Assert.Equal("created", payload["operation"]?.GetValue<string>());
        Assert.False(payload["overwritten"]!.GetValue<bool>());
        Assert.True(payload["createdDirectory"]!.GetValue<bool>());
        Assert.Equal(bytes, File.ReadAllBytes(expectedPath));
    }

    [Fact]
    public async Task PushFileTool_Execute_RejectsFileNamePaths()
    {
        using var tempDirectory = new TemporaryDirectory();

        var tool = new PushFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["directoryPath"] = "content",
            ["fileName"] = "../payload.txt",
            ["text"] = "hello"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("filesystem_push_failed", result.ErrorCode);
        Assert.Contains("file name, not a path", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CopyFileTool_Execute_CopiesFileIntoDestinationPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.WriteTextFile("source.txt", "hello");

        var tool = new CopyFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["sourcePath"] = "source.txt",
            ["destinationPath"] = Path.Combine("copies", "source-copy.txt")
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        var expectedPath = Path.Combine(tempDirectory.RootPath, "copies", "source-copy.txt");

        Assert.Equal("copied", payload["operation"]?.GetValue<string>());
        Assert.Equal("source.txt", payload["sourceRelativePath"]?.GetValue<string>());
        Assert.Equal(Path.Combine("copies", "source-copy.txt"), payload["destinationRelativePath"]?.GetValue<string>());
        Assert.Equal(expectedPath, payload["destinationFilePath"]?.GetValue<string>());
        Assert.Equal("hello", File.ReadAllText(expectedPath));
        Assert.True(File.Exists(Path.Combine(tempDirectory.RootPath, "source.txt")));
    }

    [Fact]
    public async Task MoveFileTool_Execute_MovesFileAndReportsSourceAndDestination()
    {
        using var tempDirectory = new TemporaryDirectory();
        var sourcePath = tempDirectory.WriteTextFile("source.txt", "hello");

        var tool = new MoveFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["sourcePath"] = "source.txt",
            ["destinationPath"] = Path.Combine("moved", "renamed.txt")
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        var expectedPath = Path.Combine(tempDirectory.RootPath, "moved", "renamed.txt");

        Assert.Equal("moved", payload["operation"]?.GetValue<string>());
        Assert.Equal(sourcePath, payload["sourceFilePath"]?.GetValue<string>());
        Assert.Equal(expectedPath, payload["destinationFilePath"]?.GetValue<string>());
        Assert.False(File.Exists(sourcePath));
        Assert.Equal("hello", File.ReadAllText(expectedPath));
    }

    [Fact]
    public async Task DeleteFileTool_Execute_DeletesSandboxedFile()
    {
        using var tempDirectory = new TemporaryDirectory();
        var filePath = tempDirectory.WriteTextFile("delete-me.txt", "hello");

        var tool = new DeleteFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["path"] = "delete-me.txt"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal(filePath, payload["filePath"]?.GetValue<string>());
        Assert.True(payload["deleted"]!.GetValue<bool>());
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task MoveFileTool_Execute_RejectsDestinationTraversalOutsideRoot()
    {
        using var tempDirectory = new TemporaryDirectory();
        _ = tempDirectory.WriteTextFile("source.txt", "hello");

        var tool = new MoveFileTool(CreateOptions(tempDirectory.RootPath));
        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "workspace",
            ["sourcePath"] = "source.txt",
            ["destinationPath"] = ".."
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("filesystem_move_failed", result.ErrorCode);
        Assert.Contains("outside the approved app sandbox root", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static FileSystemToolsOptions CreateOptions(string rootPath)
    {
        return FileSystemToolsOptions.CreateBuilder()
            .AddRoot("workspace", rootPath)
            .Build();
    }
}
