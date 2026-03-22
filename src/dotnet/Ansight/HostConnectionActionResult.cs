using Ansight.Pairing;

namespace Ansight;

/// <summary>
/// Result returned by runtime-owned host connection actions.
/// </summary>
public sealed record HostConnectionActionResult(
    bool Success,
    string Message,
    OpenSessionResult? SessionResult = null)
{
    public static HostConnectionActionResult FromSuccess(
        string message,
        OpenSessionResult? sessionResult = null)
        => new(true, message, sessionResult);

    public static HostConnectionActionResult FromFailure(
        string message,
        OpenSessionResult? sessionResult = null)
        => new(false, message, sessionResult);
}
