using System.Text.Json;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class DeviceAppProfileCollectorTests
{
    [Fact]
    public void Create_PopulatesCurrentProcessIdInAppProfile()
    {
        var profile = DeviceAppProfileCollector.Create();

        Assert.NotNull(profile.App);
        Assert.Equal(Environment.ProcessId, profile.App!.ProcessId);
    }

    [Fact]
    public void Create_SerializesProcessIdInAppProfilePayload()
    {
        var profile = DeviceAppProfileCollector.Create();

        var json = JsonSerializer.Serialize(profile, PairingJson.Compact);

        Assert.Contains($"\"processId\":{Environment.ProcessId}", json, StringComparison.Ordinal);
    }
}
