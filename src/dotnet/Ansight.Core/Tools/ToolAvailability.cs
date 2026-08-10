namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// Describes whether a registered tool can execute in the app's current runtime state.
/// </summary>
public sealed record ToolAvailability(
    bool IsAvailable,
    string? ReasonCode = null,
    string? Reason = null,
    string? RequiredState = null,
    string? Remediation = null,
    bool Retryable = false)
{
    /// <summary>
    /// Availability returned by tools without a runtime precondition.
    /// </summary>
    public static ToolAvailability Available { get; } = new(true);

    /// <summary>
    /// Creates an unavailable result with actionable diagnostics.
    /// </summary>
    public static ToolAvailability Unavailable(
        string reasonCode,
        string reason,
        string? requiredState = null,
        string? remediation = null,
        bool retryable = true)
        => new(false, reasonCode, reason, requiredState, remediation, retryable);

    internal JsonObject ToJson()
        => new()
        {
            ["available"] = IsAvailable,
            ["reasonCode"] = ReasonCode,
            ["reason"] = Reason,
            ["requiredState"] = RequiredState,
            ["remediation"] = Remediation,
            ["retryable"] = Retryable,
            ["evaluatedAtUtc"] = DateTimeOffset.UtcNow
        };
}

/// <summary>
/// Runtime context supplied when a tool's dynamic availability is evaluated.
/// </summary>
public sealed record ToolAvailabilityContext(string? SessionId, string? RequestId);
