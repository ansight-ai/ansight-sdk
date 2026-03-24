using Ansight.Tools;

namespace Ansight.UnitTests;

public sealed class ToolGuardTests
{
    [Fact]
    public void WithReadWriteToolAccess_EnablesReadAndWriteButNotDelete()
    {
        var options = Options.CreateBuilder()
            .WithReadWriteToolAccess()
            .Build();

        Assert.True(options.ToolGuard.DiscoveryEnabled);
        Assert.True(options.ToolGuard.ExecutionEnabled);
        Assert.Equal(new[] { ToolScope.Read, ToolScope.Write }, options.ToolGuard.AllowedScopes.OrderBy(scope => scope));
    }

    [Fact]
    public void ReadWriteGuard_CanExecuteReadAndWriteTools_ButRejectsDeleteTools()
    {
        var guard = ToolGuard.ReadWrite;

        Assert.True(guard.CanExecute(new TestTool("test.read", ToolScope.Read), out var readReason));
        Assert.Null(readReason);

        Assert.True(guard.CanExecute(new TestTool("test.write", ToolScope.Write), out var writeReason));
        Assert.Null(writeReason);

        Assert.False(guard.CanExecute(new TestTool("test.delete", ToolScope.Delete), out var deleteReason));
        Assert.Contains("not enabled", deleteReason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestTool : ITool
    {
        public TestTool(string id, ToolScope scope)
        {
            Id = id;
            Scope = scope;
        }

        public string Category => "test";

        public ToolScope Scope { get; }

        public string Id { get; }

        public string Name => Id;

        public string Description => Id;

        public string Keywords => "test";

        public ToolSchema ArgumentsSchema => ToolSchema.Object();

        public ToolSchema ResultSchema => ToolSchema.Object();

        public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
            => Task.FromResult(ToolResult.Success());
    }
}
