using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.Pairing;

namespace Ansight.DeviceProfiles;

internal sealed class DeviceAppProfileResolver
{
    private readonly IDeviceAppProfileProvider deviceAppProfileProvider;

    public DeviceAppProfileResolver(IDeviceAppProfileProvider deviceAppProfileProvider)
    {
        this.deviceAppProfileProvider = deviceAppProfileProvider;
    }

    public DeviceAppProfile? Resolve(DeviceAppProfile? callerProfile)
    {
        DeviceAppProfile? automaticProfile = null;

        try
        {
            automaticProfile = deviceAppProfileProvider.CreateDeviceAppProfile();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to create baseline DeviceAppProfile automatically: {ex.Message}");
        }

        var resolvedProfile = automaticProfile switch
        {
            null => callerProfile,
            _ when callerProfile is null => automaticProfile,
            _ => MergeDeviceAppProfiles(automaticProfile, callerProfile)
        };

        if (resolvedProfile is not null)
        {
            DeviceAppProfileCollector.EnsureSdkProfile(resolvedProfile);
        }

        return resolvedProfile;
    }

    public string? ResolveExpectedAppId(DeviceAppProfile? profile)
    {
        var appId = profile?.App?.AppId;
        return string.IsNullOrWhiteSpace(appId) ? null : appId.Trim();
    }

    public void NormalizeForSend(DeviceAppProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.Type))
        {
            profile.Type = "DeviceAppProfile";
        }

        if (string.IsNullOrWhiteSpace(profile.Schema))
        {
            profile.Schema = "ansight.device-app-profile.v1";
        }

        if (profile.SentAt <= 0)
        {
            profile.SentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        if (profile.ReasonCode <= 0)
        {
            profile.ReasonCode = 1;
        }

        if (profile.ProfileSeq <= 0)
        {
            profile.ProfileSeq = 1;
        }
    }

    private static DeviceAppProfile MergeDeviceAppProfiles(DeviceAppProfile baselineProfile, DeviceAppProfile callerProfile)
    {
        var baselineNode = JsonSerializer.SerializeToNode(baselineProfile, PairingJson.Compact)?.AsObject();
        var callerNode = JsonSerializer.SerializeToNode(callerProfile, PairingJson.Compact)?.AsObject();
        if (baselineNode is null)
        {
            return callerProfile;
        }

        if (callerNode is not null)
        {
            MergeJsonObjects(baselineNode, callerNode);
        }

        return baselineNode.Deserialize<DeviceAppProfile>(PairingJson.Compact) ?? baselineProfile;
    }

    private static void MergeJsonObjects(JsonObject target, JsonObject source)
    {
        foreach (var property in source)
        {
            if (property.Value is null)
            {
                continue;
            }

            if (property.Value is JsonObject sourceObject)
            {
                if (target[property.Key] is not JsonObject targetObject)
                {
                    target[property.Key] = sourceObject.DeepClone();
                    continue;
                }

                MergeJsonObjects(targetObject, sourceObject);
                continue;
            }

            target[property.Key] = property.Value.DeepClone();
        }
    }
}
