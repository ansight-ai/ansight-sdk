using Xunit;

namespace Ansight.Pencil.Tests;

public sealed class PencilPackageTests
{
    [Fact]
    public void CoreHasNoSimulationActivationOrDependency()
    {
        Assert.Null(typeof(Options).GetProperty("EnableSimulatorPenInput"));
        Assert.Null(typeof(Options.OptionsBuilder).GetMethod("WithSimulatorPenInput"));
        Assert.DoesNotContain(typeof(Options).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.Contains("Pencil", StringComparison.Ordinal));
        Assert.Empty(Options.CreateBuilder().Build().Tools);
    }

    [Fact]
    public void UnsupportedPlatformDoesNotRegisterToolsOrGrantPermission()
    {
        Assert.False(PencilSimulation.IsSupported);
        var builder = Options.CreateBuilder();
        Assert.Same(builder, builder.WithSimulatorPenInput());
        var options = builder.Build();
        Assert.Empty(options.Tools);
        Assert.False(options.ToolGuard.ExecutionEnabled);
    }
}
