using System.Text.Json;
using System.Reflection;
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
    public void Create_PopulatesSdkVersion()
    {
        var profile = DeviceAppProfileCollector.Create();
        var expectedVersion = typeof(global::Ansight.Runtime).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.NotNull(profile.Sdk);
        Assert.Equal("Ansight .NET SDK", profile.Sdk!.Name);
        Assert.Equal("Ansight.Core", profile.Sdk.PackageId);
        Assert.Equal("dotnet", profile.Sdk.Language);
        Assert.False(string.IsNullOrWhiteSpace(profile.Sdk.Version));
        Assert.Equal(expectedVersion, profile.Sdk.Version);
    }

    [Fact]
    public void Create_SerializesProcessIdInAppProfilePayload()
    {
        var profile = DeviceAppProfileCollector.Create();

        var json = JsonSerializer.Serialize(profile, PairingJson.Compact);

        Assert.Contains($"\"processId\":{Environment.ProcessId}", json, StringComparison.Ordinal);
        Assert.Contains("\"sdk\":{", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceApplicationIconProfile_SerializesInlineIconPayload()
    {
        var profile = new DeviceAppProfile
        {
            App = new DeviceApplicationProfile
            {
                AppId = "com.example.icon",
                Icon = new DeviceApplicationIconProfile
                {
                    Format = "png",
                    MimeType = "image/png",
                    Width = 2,
                    Height = 2,
                    ByteCount = 3,
                    DataBase64 = Convert.ToBase64String([1, 2, 3])
                }
            }
        };

        var json = JsonSerializer.Serialize(profile, PairingJson.Compact);

        Assert.Contains("\"icon\":{", json, StringComparison.Ordinal);
        Assert.Contains("\"mimeType\":\"image/png\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dataBase64\":\"AQID\"", json, StringComparison.Ordinal);
    }
}
