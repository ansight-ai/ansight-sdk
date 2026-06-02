#if MACCATALYST
using System.Runtime.InteropServices;
using UIKit;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    private const string SystemLibraryPath = "/usr/lib/libSystem.dylib";
    private const string HardwareModelSysctlName = "hw.model";
    private const string HardwareMemorySizeSysctlName = "hw.memsize";
    private const string CpuBrandStringSysctlName = "machdep.cpu.brand_string";

    private static partial void PopulateDeviceProfile(DeviceProfile profile)
    {
        PopulateAppleDeviceProfile(
            profile,
            isDesktop: true,
            osName: "maccatalyst");
        NormalizeMacCatalystDesktopDeviceProfile(profile);
    }

    private static partial void PopulateApplicationProfile(DeviceApplicationProfile profile)
    {
        PopulateAppleApplicationProfile(profile);
    }

    private static partial int ResolvePlatformRuntimeCode() => 3;

    private static partial string ResolveRuntimePlatformName() => "maccatalyst";

    private static partial string? ResolvePlatformVersion() => NullIfWhiteSpace(UIDevice.CurrentDevice.SystemVersion);

    // Keep desktop hardware reads in this MACCATALYST partial so they are not compiled into iOS SDK variants.
    private static void NormalizeMacCatalystDesktopDeviceProfile(DeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.Model = ResolveMacCatalystHardwareModel();
        profile.FormFactor = DeviceFormFactors.Desktop;
        profile.DeviceClassCode = ResolveAppleOrAndroidDeviceClassCode(isDesktop: true);
        profile.ChipModel = ReadMacCatalystSysctlString(CpuBrandStringSysctlName) ?? profile.ChipModel;
        profile.MemoryTotalMb = ReadMacCatalystMemoryTotalMb() ?? profile.MemoryTotalMb;
    }

    private static string ResolveMacCatalystHardwareModel()
    {
        return ReadMacCatalystSysctlString(HardwareModelSysctlName)
               ?? NormalizeMacCatalystFallbackModel(UIDevice.CurrentDevice.Model);
    }

    private static string NormalizeMacCatalystFallbackModel(string? model)
    {
        var normalizedModel = NullIfWhiteSpace(model);
        if (normalizedModel is null
            || string.Equals(normalizedModel, "iPad", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedModel, "iPhone", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedModel, "iPod", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedModel, "iPod touch", StringComparison.OrdinalIgnoreCase))
        {
            return "Mac";
        }

        return normalizedModel;
    }

    private static long? ReadMacCatalystMemoryTotalMb()
    {
        var memoryBytes = ReadMacCatalystSysctlInt64(HardwareMemorySizeSysctlName);
        return memoryBytes.GetValueOrDefault() > 0
            ? memoryBytes / 1024 / 1024
            : null;
    }

    private static string? ReadMacCatalystSysctlString(string name)
    {
        var valueLength = UIntPtr.Zero;
        if (sysctlbyname(name, IntPtr.Zero, ref valueLength, IntPtr.Zero, UIntPtr.Zero) != 0
            || valueLength == UIntPtr.Zero)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)valueLength);
        try
        {
            if (sysctlbyname(name, buffer, ref valueLength, IntPtr.Zero, UIntPtr.Zero) != 0)
            {
                return null;
            }

            return NullIfWhiteSpace(Marshal.PtrToStringAnsi(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static long? ReadMacCatalystSysctlInt64(string name)
    {
        var valueLength = (UIntPtr)sizeof(long);
        var buffer = Marshal.AllocHGlobal(sizeof(long));
        try
        {
            return sysctlbyname(name, buffer, ref valueLength, IntPtr.Zero, UIntPtr.Zero) == 0
                   && valueLength == (UIntPtr)sizeof(long)
                ? Marshal.ReadInt64(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport(SystemLibraryPath, EntryPoint = "sysctlbyname", SetLastError = true)]
    private static extern int sysctlbyname(
        string name,
        IntPtr oldPointer,
        ref UIntPtr oldLength,
        IntPtr newPointer,
        UIntPtr newLength);
}
#endif
