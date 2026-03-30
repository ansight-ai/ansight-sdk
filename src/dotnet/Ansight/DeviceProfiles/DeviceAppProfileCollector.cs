using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    public static DeviceAppProfile Create()
    {
        return new DeviceAppProfile
        {
            SentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ReasonCode = 1,
            ProfileSeq = 1,
            Device = CreateDeviceProfile(),
            App = CreateApplicationProfile(),
            Runtime = CreateRuntimeProfile(),
            Tags =
            [
                "ansight-baseline-profile"
            ]
        };
    }

    private static DeviceProfile CreateDeviceProfile()
    {
        var profile = new DeviceProfile
        {
            Locale = NullIfWhiteSpace(CultureInfo.CurrentUICulture.Name),
            TimeZone = NullIfWhiteSpace(TimeZoneInfo.Local.Id),
            CpuArch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            CpuCoreCount = global::System.Environment.ProcessorCount
        };

        PopulateDeviceProfile(profile);
        return profile;
    }

    private static DeviceApplicationProfile CreateApplicationProfile()
    {
        var assemblyVersion = ResolveAssemblyVersion();
        var profile = new DeviceApplicationProfile
        {
            ProcessId = global::System.Environment.ProcessId,
            EnvironmentCode = ResolveEnvironmentCode(),
            Debuggable = ResolveIsDebuggable(),
            VersionName = assemblyVersion,
            VersionCode = assemblyVersion,
            BuildNumber = assemblyVersion
        };

        PopulateApplicationProfile(profile);
        return profile;
    }

    private static DeviceRuntimeProfile CreateRuntimeProfile()
    {
        return new DeviceRuntimeProfile
        {
            Primary = 250,
            PrimaryVersion = ResolveAssemblyVersion(),
            Engine = new DeviceRuntimeEngineProfile
            {
                Name = "dotnet",
                Version = global::System.Environment.Version.ToString()
            },
            Stack =
            [
                new DeviceRuntimeStackEntry
                {
                    RuntimeCode = 250,
                    Name = "dotnet",
                    Version = global::System.Environment.Version.ToString()
                },
                new DeviceRuntimeStackEntry
                {
                    RuntimeCode = ResolvePlatformRuntimeCode(),
                    Name = ResolveRuntimePlatformName(),
                    Version = ResolvePlatformVersion()
                }
            ],
            AotEnabled = !RuntimeFeature.IsDynamicCodeCompiled,
            JitEnabled = RuntimeFeature.IsDynamicCodeSupported
        };
    }

    private static string? ResolveAssemblyVersion()
    {
        return NullIfWhiteSpace(Assembly.GetEntryAssembly()?.GetName().Version?.ToString())
               ?? NullIfWhiteSpace(Assembly.GetExecutingAssembly().GetName().Version?.ToString());
    }

    private static int ResolveEnvironmentCode()
    {
#if DEBUG
        return 2;
#else
        return 1;
#endif
    }

    private static bool ResolveIsDebuggable()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static string ResolveFallbackOsName()
    {
        if (OperatingSystem.IsMacCatalyst())
        {
            return "maccatalyst";
        }

        if (OperatingSystem.IsIOS())
        {
            return "ios";
        }

        if (OperatingSystem.IsAndroid())
        {
            return "android";
        }

        return global::System.Environment.OSVersion.Platform.ToString().ToLowerInvariant();
    }

    private static int ResolveAppleOrAndroidDeviceClassCode(bool isDesktop)
    {
        return isDesktop ? 3 : 1;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static partial void PopulateDeviceProfile(DeviceProfile profile);

    private static partial void PopulateApplicationProfile(DeviceApplicationProfile profile);

    private static partial int ResolvePlatformRuntimeCode();

    private static partial string ResolveRuntimePlatformName();

    private static partial string? ResolvePlatformVersion();
}
