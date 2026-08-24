using Ansight.Tools;

namespace Ansight.UnitTests;

public sealed class ToolGuardTests
{
    [Fact]
    public void WithReadWriteToolAccess_SetsWriteAsMaximumPolicy()
    {
        var options = Options.CreateBuilder()
            .WithReadWriteToolAccess()
            .Build();

        Assert.True(options.ToolGuard.DiscoveryEnabled);
        Assert.True(options.ToolGuard.ExecutionEnabled);
        Assert.Equal(ToolPolicy.Write, options.ToolGuard.MaxPolicy);
    }

    [Fact]
    public void ReadWriteGuard_CanExecuteReadAndWriteTools_ButRejectsCriticalTools()
    {
        var guard = ToolGuard.ReadWrite;

        Assert.True(guard.CanExecute(new TestTool("test.read", ToolPolicy.Read), out var readReason));
        Assert.Null(readReason);

        Assert.True(guard.CanExecute(new TestTool("test.write", ToolPolicy.Write), out var writeReason));
        Assert.Null(writeReason);

        Assert.False(guard.CanExecute(new TestTool("test.critical", ToolPolicy.Critical), out var criticalReason));
        Assert.Contains("exceeds", criticalReason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestTool : ITool
    {
        public TestTool(string id, ToolPolicy policy)
        {
            Id = id;
            Policy = policy;
        }

        public string Category => "test";

        public ToolPolicy Policy { get; }

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
