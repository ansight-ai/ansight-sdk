using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class DeviceAppProfileResolverTests
{
    [Fact]
    public void Resolve_MergesCallerProfileOverAutomaticProfile()
    {
        var automaticProfile = new DeviceAppProfile
        {
            Device = new DeviceProfile
            {
                Manufacturer = "Apple",
                Model = "iPhone"
            },
            App = new DeviceApplicationProfile
            {
                AppId = "com.ansight.test",
                AppName = "Ansight",
                ProcessId = 101
            },
            Permissions = new Dictionary<string, string>
            {
                ["camera"] = "allowed"
            }
        };

        var callerProfile = new DeviceAppProfile
        {
            Device = new DeviceProfile
            {
                Model = "iPhone 16 Pro"
            },
            App = new DeviceApplicationProfile
            {
                AppName = "Caller Override"
            },
            Tags =
            [
                "caller-tag"
            ]
        };

        var sut = new DeviceAppProfileResolver(new StubDeviceAppProfileProvider(automaticProfile));

        var merged = sut.Resolve(callerProfile);

        Assert.NotNull(merged);
        Assert.Equal("Apple", merged!.Device!.Manufacturer);
        Assert.Equal("iPhone 16 Pro", merged.Device.Model);
        Assert.Equal("com.ansight.test", merged.App!.AppId);
        Assert.Equal("Caller Override", merged.App.AppName);
        Assert.Equal(101, merged.App.ProcessId);
        Assert.Single(merged.Tags!);
        Assert.Equal("caller-tag", merged.Tags![0]);
        Assert.Equal("allowed", merged.Permissions!["camera"]);
    }

    [Fact]
    public void NormalizeForSend_FillsMissingDefaults()
    {
        var profile = new DeviceAppProfile
        {
            Type = string.Empty,
            Schema = string.Empty,
            SentAt = 0,
            ReasonCode = 0,
            ProfileSeq = 0
        };

        var sut = new DeviceAppProfileResolver(new StubDeviceAppProfileProvider(profile: null));

        sut.NormalizeForSend(profile);

        Assert.Equal("DeviceAppProfile", profile.Type);
        Assert.Equal("ansight.device-app-profile.v1", profile.Schema);
        Assert.True(profile.SentAt > 0);
        Assert.Equal(1, profile.ReasonCode);
        Assert.Equal(1, profile.ProfileSeq);
    }

    private sealed class StubDeviceAppProfileProvider : IDeviceAppProfileProvider
    {
        private readonly DeviceAppProfile? profile;

        public StubDeviceAppProfileProvider(DeviceAppProfile? profile)
        {
            this.profile = profile;
        }

        public DeviceAppProfile? CreateDeviceAppProfile() => profile;
    }
}
