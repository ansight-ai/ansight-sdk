using System.Diagnostics;
using System.Net.Http.Headers;

namespace Ansight.Network;

/// <summary>
/// Opt-in <see cref="DelegatingHandler"/> that records HTTP request data in Ansight.
/// Text bodies are included by default with a bounded, app-controlled policy.
/// Credential-bearing headers, URLs, and text bodies are sanitized before capture.
/// </summary>
public sealed class AnsightHttpMessageHandler : DelegatingHandler
{
    private const int temporaryFileThresholdBytes = 1024 * 1024;
    internal const string InternalTrafficHeaderName = "X-Ansight-Internal-Traffic";
    internal static readonly HttpRequestOptionsKey<bool> ExplicitCaptureMarker =
        new("Ansight.ExplicitNetworkCapture");
    internal static readonly HttpRequestOptionsKey<bool> InternalTrafficMarker =
        new("Ansight.InternalNetworkTraffic");
    private readonly NetworkRequestSanitizationOptions sanitizationOptions;

    /// <summary>
    /// Creates a handler backed by a default <see cref="HttpClientHandler"/>.
    /// </summary>
    public AnsightHttpMessageHandler()
        : this(new HttpClientHandler(), new NetworkRequestSanitizationOptions())
    {
    }

    /// <summary>
    /// Creates a handler backed by a default <see cref="HttpClientHandler"/> and app-side privacy controls.
    /// </summary>
    public AnsightHttpMessageHandler(NetworkRequestSanitizationOptions sanitizationOptions)
        : this(new HttpClientHandler(), sanitizationOptions)
    {
    }

    /// <summary>
    /// Creates a handler with fluent body-capture and sanitization controls.
    /// </summary>
    public AnsightHttpMessageHandler(Action<NetworkRequestSanitizationOptionsBuilder> configure)
        : this(new HttpClientHandler(), BuildOptions(configure))
    {
    }

    /// <summary>
    /// Creates a handler wrapping an application-provided inner handler.
    /// </summary>
    public AnsightHttpMessageHandler(HttpMessageHandler innerHandler)
        : this(innerHandler, new NetworkRequestSanitizationOptions())
    {
    }

    /// <summary>
    /// Creates a handler wrapping an application-provided inner handler with app-side privacy controls.
    /// </summary>
    public AnsightHttpMessageHandler(
        HttpMessageHandler innerHandler,
        NetworkRequestSanitizationOptions sanitizationOptions)
        : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
    {
        this.sanitizationOptions = sanitizationOptions
                                   ?? throw new ArgumentNullException(nameof(sanitizationOptions));
    }

    /// <summary>
    /// Creates a handler wrapping an application-provided inner handler with fluent privacy controls.
    /// </summary>
    public AnsightHttpMessageHandler(
        HttpMessageHandler innerHandler,
        Action<NetworkRequestSanitizationOptionsBuilder> configure)
        : this(innerHandler, BuildOptions(configure))
    {
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsInternalTraffic(request))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        request.Options.Set(ExplicitCaptureMarker, true);
        if (!Runtime.HostConnection.IsConnected)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var startedTimestamp = Stopwatch.GetTimestamp();
        HttpResponseMessage? response = null;
        Exception? failure = null;
        NetworkBody? requestBody = null;
        NetworkBody? responseBody = null;

        try
        {
            requestBody = await CaptureBodyAsync(
                    request.Content,
                    sanitizationOptions.CaptureRequestBody,
                    cancellationToken)
                .ConfigureAwait(false);
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (Runtime.HostConnection.IsConnected)
            {
                responseBody = await CaptureBodyAsync(
                        response.Content,
                        sanitizationOptions.CaptureResponseBody,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            return response;
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (Runtime.HostConnection.IsConnected)
            {
                var completedAtUtc = DateTimeOffset.UtcNow;
                var record = NetworkRequestSanitizer.Sanitize(new NetworkRequestRecord
                {
                    Id = Guid.CreateVersion7().ToString("N"),
                    Source = "dotnet.httpclient",
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    DurationMilliseconds = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                    Method = request.Method.Method,
                    Url = request.RequestUri?.ToString() ?? "<unknown>",
                    Protocol = response?.Version.ToString(),
                    RequestHeaders = CaptureHeaders(request.Headers, request.Content?.Headers),
                    RequestBodySizeBytes = request.Content?.Headers.ContentLength,
                    RequestBody = requestBody,
                    StatusCode = response is null ? null : (int)response.StatusCode,
                    ReasonPhrase = response?.ReasonPhrase,
                    ResponseHeaders = CaptureHeaders(response?.Headers, response?.Content?.Headers),
                    ResponseBodySizeBytes = response?.Content?.Headers.ContentLength,
                    ResponseBody = responseBody,
                    ErrorType = failure?.GetType().FullName,
                    ErrorMessage = failure?.Message
                }, sanitizationOptions);
                if (record is not null)
                {
                    Runtime.RecordNetworkRequest(record);
                }
            }
        }
    }

    internal static void MarkAsInternalTraffic(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(InternalTrafficMarker, true);
#if IOS || MACCATALYST
        // NSURLProtocol cannot see HttpRequestMessage.Options. The native interceptor consumes
        // and removes this private marker before forwarding, so it never reaches the server.
        if (Runtime.IsInitialized
            && Runtime.MutableInstance.Options.ResolveNetworkCaptureOptions() is not null)
        {
            request.Headers.TryAddWithoutValidation(InternalTrafficHeaderName, "1");
        }
#endif
    }

    internal static bool IsInternalTraffic(HttpRequestMessage request)
        => request.Options.TryGetValue(InternalTrafficMarker, out var internalTraffic)
           && internalTraffic
           || request.Headers.Contains(InternalTrafficHeaderName);

    private static NetworkRequestSanitizationOptions BuildOptions(
        Action<NetworkRequestSanitizationOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new NetworkRequestSanitizationOptionsBuilder();
        configure(builder);
        return builder.Build();
    }

    private async Task<NetworkBody?> CaptureBodyAsync(
        HttpContent? content,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!enabled || content is null)
        {
            return null;
        }

        var maximumBytes = NetworkRequestSanitizer.MaximumBodyBytes(sanitizationOptions);
        var totalBytes = content.Headers.ContentLength;
        if (maximumBytes <= 0
            || totalBytes is null or <= 0)
        {
            // Do not consume unknown-length streams. Capture must never alter app I/O.
            return null;
        }

        var contentType = content.Headers.ContentType?.ToString();
        var binary = !NetworkRequestSanitizer.IsTextContentType(contentType);
        if (binary && !sanitizationOptions.CaptureBinaryBodies)
        {
            return null;
        }

        try
        {
            if (totalBytes.Value > temporaryFileThresholdBytes)
            {
                var captured = await CaptureLargeSeekableBodyAsync(
                        content,
                        maximumBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                return captured is null
                    ? null
                    : NetworkRequestSanitizer.CreateBody(
                        captured,
                        totalBytes,
                        contentType,
                        binary,
                        sanitizationOptions);
            }

            // HttpContent retains the buffered representation, so downstream consumers can still read it.
            var bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return NetworkRequestSanitizer.CreateBody(
                bytes,
                totalBytes,
                contentType,
                binary,
                sanitizationOptions);
        }
        catch
        {
            // Observability must never change whether the application's request succeeds.
            return null;
        }
    }

    private static async Task<byte[]?> CaptureLargeSeekableBodyAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (!source.CanSeek)
        {
            return null;
        }

        var originalPosition = source.Position;
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"ansight-network-{Guid.CreateVersion7():N}.tmp");
        try
        {
            await using var temporary = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            var buffer = new byte[Math.Min(81_920, maximumBytes)];
            var remaining = maximumBytes;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(
                        buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await temporary.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }

            temporary.Position = 0;
            var captured = new byte[temporary.Length];
            await temporary.ReadExactlyAsync(captured, cancellationToken).ConfigureAwait(false);
            return captured;
        }
        finally
        {
            source.Position = originalPosition;
        }
    }

    private static IReadOnlyList<NetworkHeader> CaptureHeaders(params HttpHeaders?[] sources)
    {
        var headers = new List<NetworkHeader>();
        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var header in source)
            {
                foreach (var value in header.Value)
                {
                    headers.Add(new NetworkHeader
                    {
                        Name = header.Key,
                        Value = value
                    });
                }
            }
        }

        return NetworkRequestSanitizer.SanitizeHeaders(headers);
    }
}
