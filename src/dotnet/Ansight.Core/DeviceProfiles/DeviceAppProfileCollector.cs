using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    private const int MaxApplicationIconPixelLength = 256;
    private const int MaxApplicationIconByteCount = 2 * 1024 * 1024;

    public static DeviceAppProfile Create()
    {
        return new DeviceAppProfile
        {
            SentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ReasonCode = 1,
            ProfileSeq = 1,
            Sdk = CreateSdkProfile(),
            Device = CreateDeviceProfile(),
            App = CreateApplicationProfile(),
            Runtime = CreateRuntimeProfile(),
            Tags =
            [
                "ansight-baseline-profile"
            ]
        };
    }

    internal static DeviceSdkProfile CreateSdkProfile()
    {
        return new DeviceSdkProfile
        {
            Name = "Ansight .NET SDK",
            PackageId = "Ansight.Core",
            Version = ResolveSdkVersion(),
            Language = "dotnet"
        };
    }

    internal static void EnsureSdkProfile(DeviceAppProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var sdk = profile.Sdk ??= CreateSdkProfile();
        sdk.Name = NullIfWhiteSpace(sdk.Name) ?? "Ansight .NET SDK";
        sdk.PackageId = NullIfWhiteSpace(sdk.PackageId) ?? "Ansight.Core";
        sdk.Version = NullIfWhiteSpace(sdk.Version) ?? ResolveSdkVersion();
        sdk.Language = NullIfWhiteSpace(sdk.Language) ?? "dotnet";
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

    private static string? ResolveSdkVersion()
    {
        var assembly = typeof(DeviceAppProfileCollector).Assembly;
        return NullIfWhiteSpace(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
               ?? NullIfWhiteSpace(assembly.GetName().Version?.ToString());
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

    private static DeviceApplicationIconProfile? CreateApplicationIconProfile(
        byte[]? bytes,
        string format,
        string mimeType,
        int? width = null,
        int? height = null)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxApplicationIconByteCount)
        {
            return null;
        }

        return new DeviceApplicationIconProfile
        {
            Format = NullIfWhiteSpace(format) ?? "png",
            MimeType = NullIfWhiteSpace(mimeType) ?? "image/png",
            Width = width.GetValueOrDefault() > 0 ? width : null,
            Height = height.GetValueOrDefault() > 0 ? height : null,
            ByteCount = bytes.LongLength,
            DataBase64 = Convert.ToBase64String(bytes)
        };
    }

    private static ApplicationIconDimensions ResolveApplicationIconDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return new ApplicationIconDimensions(MaxApplicationIconPixelLength, MaxApplicationIconPixelLength);
        }

        var longestSide = Math.Max(width, height);
        if (longestSide <= MaxApplicationIconPixelLength)
        {
            return new ApplicationIconDimensions(width, height);
        }

        var scale = MaxApplicationIconPixelLength / (double)longestSide;
        return new ApplicationIconDimensions(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
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

    private readonly record struct ApplicationIconDimensions(int Width, int Height);
}
