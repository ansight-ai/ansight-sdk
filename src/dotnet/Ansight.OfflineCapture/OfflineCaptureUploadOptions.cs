namespace Ansight.OfflineCapture;

/// <summary>
/// Configures an offline capture upload to an Ansight team app.
/// </summary>
public sealed class OfflineCaptureUploadOptions
{
    /// <summary>
    /// The production Ansight capture upload endpoint.
    /// </summary>
    public static readonly Uri DefaultEndpoint =
        new("https://app.ansight.ai/submit_capture");

    /// <summary>
    /// A revocable, app-scoped capture API key issued from the Ansight portal.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Overrides the upload endpoint, primarily for self-hosted or development environments.
    /// </summary>
    public Uri Endpoint { get; set; } = DefaultEndpoint;

    /// <summary>
    /// Selects a persisted capture session. The latest stopped session is used when omitted.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Optional title shown in the Ansight portal.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Maximum attempts for transient API and object-storage failures.
    /// </summary>
    public int MaximumAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay used for exponential retry backoff.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    internal OfflineCaptureUploadOptions Normalize()
    {
        var normalizedApiKey = ApiKey?.Trim() ?? string.Empty;
        if (!normalizedApiKey.StartsWith("an_cap_", StringComparison.Ordinal)
            || normalizedApiKey.Length < 32)
        {
            throw new ArgumentException(
                "ApiKey must be an Ansight capture API key issued from the team app.",
                nameof(ApiKey));
        }

        if (Endpoint is null || !Endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(Endpoint));
        }

        if (Endpoint.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("Endpoint must use HTTP or HTTPS.", nameof(Endpoint));
        }

        if (MaximumAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumAttempts),
                MaximumAttempts,
                "MaximumAttempts must be between 1 and 10.");
        }

        if (RetryDelay < TimeSpan.Zero || RetryDelay > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetryDelay),
                RetryDelay,
                "RetryDelay must be between zero and one minute.");
        }

        return new OfflineCaptureUploadOptions
        {
            ApiKey = normalizedApiKey,
            Endpoint = Endpoint,
            SessionId = NormalizeOptional(SessionId),
            Title = NormalizeOptional(Title),
            MaximumAttempts = MaximumAttempts,
            RetryDelay = RetryDelay
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
