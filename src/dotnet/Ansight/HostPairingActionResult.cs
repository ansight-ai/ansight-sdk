namespace Ansight;

/// <summary>
/// Result returned by runtime-owned host pairing actions.
/// </summary>
public sealed record HostPairingActionResult(
    bool Success,
    string Message)
{
    public static HostPairingActionResult FromSuccess(string message)
        => new(true, message);

    public static HostPairingActionResult FromFailure(string message)
        => new(false, message);
}
