namespace Ansight.DeviceProfiles;

public interface IDeviceAppProfileProvider
{
    DeviceAppProfile? CreateDeviceAppProfile();
}

internal sealed class AutomaticDeviceAppProfileProvider : IDeviceAppProfileProvider
{
    public static AutomaticDeviceAppProfileProvider Instance { get; } = new();

    public DeviceAppProfile? CreateDeviceAppProfile()
    {
        return DeviceAppProfileCollector.Create();
    }
}
