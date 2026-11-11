using System.Reflection;
using Avalonia;
using Xunit;

namespace Ansight.Avalonia.Tests;

public sealed class AvaloniaSessionPropertiesTests
{
    [Fact]
    public void MetadataUsesLoadedFrameworkAndConfiguredSubsystems()
    {
        var builder = AppBuilder.Configure<global::Avalonia.Application>()
            .UseRenderingSubsystem(() => { }, "TestRenderer")
            .UseWindowingSubsystem(() => { }, "TestWindowing");

        var properties = AvaloniaSessionProperties.CreateValues(builder, typeof(global::Avalonia.Application));
        var version = typeof(global::Avalonia.Application).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        Assert.Equal("Avalonia", properties["framework"]);
        Assert.Equal(version, properties["version"]);
        Assert.Equal("Avalonia.Application", properties["applicationType"]);
        Assert.Equal("TestRenderer", properties["renderingSubsystem"]);
        Assert.Equal("TestWindowing", properties["windowingSubsystem"]);
        Assert.False(string.IsNullOrWhiteSpace(properties["sdkVersion"]));
    }

    [Fact]
    public void UnavailableSubsystemAndLifetimeMetadataIsNotInvented()
    {
        var builder = AppBuilder.Configure<global::Avalonia.Application>();

        var properties = AvaloniaSessionProperties.CreateValues(builder, typeof(global::Avalonia.Application));

        Assert.False(properties.ContainsKey("applicationLifetime"));
        Assert.False(properties.ContainsKey("renderingSubsystem"));
        Assert.False(properties.ContainsKey("windowingSubsystem"));
    }
}
