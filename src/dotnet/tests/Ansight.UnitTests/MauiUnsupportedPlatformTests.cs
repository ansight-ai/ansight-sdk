using Ansight.Maui;
using Microsoft.Maui.Hosting;

namespace Ansight.UnitTests;

public sealed class MauiUnsupportedPlatformTests
{
    [Fact]
    public void UseAnsightIsANoopOnTheNeutralTarget()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        var services = builder.Services.ToArray();
        var called = false;
        Assert.Same(builder, builder.UseAnsight(_ => called = true));
        Assert.Same(builder, builder.UseAnsight<Microsoft.Maui.Controls.Application>(_ => called = true));
        Assert.Same(builder, builder.UseAnsight(Options.CreateBuilder().Build()));
        Assert.False(called);
        Assert.Equal(services, builder.Services.ToArray());
    }
}
