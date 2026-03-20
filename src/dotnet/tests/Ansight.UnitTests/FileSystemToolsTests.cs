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
            ["files.list_directory", "files.read_file"],
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

        Assert.Contains(Path.Combine("content", "readme.txt"), relativePaths);
        Assert.Contains(Path.Combine("content", "nested"), relativePaths);
        Assert.Contains(Path.Combine("content", "nested", "notes.txt"), relativePaths);
        Assert.DoesNotContain(Path.Combine("content", ".secret.txt"), relativePaths);
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
        Assert.Equal("hello", payload["text"]?.GetValue<string>());
        Assert.True(payload["truncated"]!.GetValue<bool>());
        Assert.Equal(5, payload["bytesRead"]!.GetValue<int>());
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
        Assert.Equal(Convert.ToBase64String(bytes), payload["base64"]?.GetValue<string>());
    }

    private static FileSystemToolsOptions CreateOptions(string rootPath)
    {
        return FileSystemToolsOptions.CreateBuilder()
            .AddRoot("workspace", rootPath)
            .Build();
    }
}
