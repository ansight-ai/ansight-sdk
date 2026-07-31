#if ANDROID
using Android.OS;
#endif
#if IOS
using Foundation;
using ObjCRuntime;
#endif

namespace Ansight.Pairing;

internal static class PairingSimulatorLocalHostAddress
{
    public static string? Resolve()
    {
        try
        {
            return NullIfWhiteSpace(ResolveCore());
        }
        catch (Exception ex)
        {
            Logger.Info($"Simulator host-address detection failed: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveCore()
    {
#if ANDROID
        return ResolveAndroidIsEmulator()
            ? ResolveAndroidHostAddress()
            : null;
#elif IOS
        return ResolveAppleIsSimulator()
            ? "127.0.0.1"
            : null;
#elif MACCATALYST || MACOS
        return "127.0.0.1";
#else
        return "127.0.0.1";
#endif
    }

#if ANDROID
    private static string ResolveAndroidHostAddress()
    {
        var manufacturer = Build.Manufacturer ?? string.Empty;
        return manufacturer.Contains("Genymotion", StringComparison.OrdinalIgnoreCase)
            ? "10.0.3.2"
            : "10.0.2.2";
    }

    private static bool ResolveAndroidIsEmulator()
    {
        var fingerprint = Build.Fingerprint ?? string.Empty;
        var model = Build.Model ?? string.Empty;
        var product = Build.Product ?? string.Empty;
        var manufacturer = Build.Manufacturer ?? string.Empty;
        var brand = Build.Brand ?? string.Empty;
        var device = Build.Device ?? string.Empty;

        return fingerprint.Contains("generic", StringComparison.OrdinalIgnoreCase)
               || fingerprint.Contains("emulator", StringComparison.OrdinalIgnoreCase)
               || model.Contains("Emulator", StringComparison.OrdinalIgnoreCase)
               || model.Contains("Android SDK built for", StringComparison.OrdinalIgnoreCase)
               || manufacturer.Contains("Genymotion", StringComparison.OrdinalIgnoreCase)
               || (brand.StartsWith("generic", StringComparison.OrdinalIgnoreCase)
                   && device.StartsWith("generic", StringComparison.OrdinalIgnoreCase))
               || product.Contains("sdk", StringComparison.OrdinalIgnoreCase);
    }
#endif

#if IOS
    private static bool ResolveAppleIsSimulator()
    {
        if (ObjCRuntime.Runtime.Arch == Arch.SIMULATOR)
        {
            return true;
        }

        return HasSimulatorEnvironmentValue("SIMULATOR_DEVICE_NAME")
               || HasSimulatorEnvironmentValue("SIMULATOR_MODEL_IDENTIFIER")
               || HasSimulatorEnvironmentValue("SIMULATOR_UDID");
    }

    private static bool HasSimulatorEnvironmentValue(string name)
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
               || !string.IsNullOrWhiteSpace(NSProcessInfo.ProcessInfo.Environment[name]?.ToString());
    }
#endif

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
