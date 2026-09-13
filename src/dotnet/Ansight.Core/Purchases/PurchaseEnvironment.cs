namespace Ansight.Purchases;

/// <summary>Fail-closed platform preflight. Observation labels never authorize interop.</summary>
internal static class PurchaseEnvironment
{
    internal static bool IsAllowed()
    {
        try {
#if IOS
            return ObjCRuntime.Runtime.Arch == ObjCRuntime.Arch.SIMULATOR;
#elif ANDROID
            return IsAndroidEmulator(Android.OS.Build.Hardware);
#else
            return false;
#endif
        } catch { return false; }
    }

    internal static bool IsAndroidEmulator(string? hardware) => hardware is "goldfish" or "ranchu";
}

/// <summary>Purchase interop requires a recognized simulator or emulator.</summary>
public sealed class PurchaseEnvironmentException : InvalidOperationException
{
    public const string ErrorCode = "purchases_environment_not_allowed";
    public PurchaseEnvironmentException() : base("Purchase interop is restricted to simulators and emulators.") { }
}
