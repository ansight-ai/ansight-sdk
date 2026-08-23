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
    public void Create_PopulatesFirstClassDeviceFormFactorAndVirtualState()
    {
        var profile = DeviceAppProfileCollector.Create();

        Assert.NotNull(profile.Device);
        Assert.Equal(DeviceFormFactors.Desktop, profile.Device!.FormFactor);
        Assert.False(profile.Device.IsVirtual);
        Assert.False(profile.Device.IsEmulator);
    }

    [Fact]
    public void Create_PopulatesNormalizedLocalizationFields()
    {
        var profile = DeviceAppProfileCollector.Create();
        var culture = System.Globalization.CultureInfo.CurrentUICulture;

        Assert.NotNull(profile.Device);
        Assert.Equal(
            string.IsNullOrWhiteSpace(culture.Name) ? null : culture.Name,
            profile.Device!.Locale);
        Assert.Equal(TimeZoneInfo.Local.Id, profile.Device.TimeZone);
        Assert.NotNull(profile.Device.UtcOffsetMinutes);
        Assert.InRange(profile.Device.UtcOffsetMinutes.Value, -14 * 60, 14 * 60);

        if (!culture.Equals(System.Globalization.CultureInfo.InvariantCulture))
        {
            Assert.Equal(culture.TwoLetterISOLanguageName, profile.Device.Language);
        }
    }

    [Fact]
    public void DeviceProfile_SerializesFirstClassFormFactorAndVirtualState()
    {
        var profile = new DeviceAppProfile
        {
            Device = new DeviceProfile
            {
                FormFactor = DeviceFormFactors.Tablet,
                IsVirtual = true,
                IsEmulator = true
            }
        };

        var json = JsonSerializer.Serialize(profile, PairingJson.Compact);

        Assert.Contains("\"formFactor\":\"tablet\"", json, StringComparison.Ordinal);
        Assert.Contains("\"isVirtual\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"isEmulator\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceProfile_SerializesLocalizationFields()
    {
        var profile = new DeviceAppProfile
        {
            Device = new DeviceProfile
            {
                Locale = "en-AU",
                Language = "en",
                Region = "AU",
                TimeZone = "Australia/Sydney",
                UtcOffsetMinutes = 600
            }
        };

        var json = JsonSerializer.Serialize(profile, PairingJson.Compact);

        Assert.Contains("\"locale\":\"en-AU\"", json, StringComparison.Ordinal);
        Assert.Contains("\"language\":\"en\"", json, StringComparison.Ordinal);
        Assert.Contains("\"region\":\"AU\"", json, StringComparison.Ordinal);
        Assert.Contains("\"timeZone\":\"Australia/Sydney\"", json, StringComparison.Ordinal);
        Assert.Contains("\"utcOffsetMinutes\":600", json, StringComparison.Ordinal);
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
