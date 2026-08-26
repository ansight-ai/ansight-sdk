using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;

namespace Ansight.Network;

/// <summary>
/// Observes the System.Net.Http diagnostic source so platform-backed HttpClient traffic can be
/// captured without replacing application handlers. Diagnostic observation is metadata-only and
/// never consumes request or response bodies.
/// </summary>
internal sealed class AutomaticHttpClientNetworkCapture :
    IObserver<DiagnosticListener>,
    IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private const string httpDiagnosticListenerName = "HttpHandlerDiagnosticListener";
    private const string requestStartEventName = "System.Net.Http.HttpRequestOut.Start";
    private const string requestStopEventName = "System.Net.Http.HttpRequestOut.Stop";
    private const string requestExceptionEventName = "System.Net.Http.Exception";

    private readonly Func<bool> shouldCapture;
    private readonly Action<NetworkRequestRecord> recordRequest;
    private readonly NetworkRequestSanitizationOptions sanitizationOptions;
    private readonly ConcurrentDictionary<HttpRequestMessage, PendingRequest> pendingRequests = new();
    private readonly List<IDisposable> listenerSubscriptions = [];
    private readonly object subscriptionsLock = new();
    private readonly IDisposable allListenersSubscription;
    private bool disposed;

    public AutomaticHttpClientNetworkCapture(
        Func<bool> shouldCapture,
        Action<NetworkRequestRecord> recordRequest,
        NetworkRequestSanitizationOptions sanitizationOptions)
    {
        this.shouldCapture = shouldCapture ?? throw new ArgumentNullException(nameof(shouldCapture));
        this.recordRequest = recordRequest ?? throw new ArgumentNullException(nameof(recordRequest));
        this.sanitizationOptions = sanitizationOptions
                                   ?? throw new ArgumentNullException(nameof(sanitizationOptions));
        allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (disposed || !string.Equals(listener.Name, httpDiagnosticListenerName, StringComparison.Ordinal))
        {
            return;
        }

        var subscription = listener.Subscribe(this);
        lock (subscriptionsLock)
        {
            if (disposed)
            {
                subscription.Dispose();
                return;
            }

            listenerSubscriptions.Add(subscription);
        }
    }

    public void OnNext(KeyValuePair<string, object?> diagnosticEvent)
    {
        try
        {
            switch (diagnosticEvent.Key)
            {
                case requestStartEventName:
                    StartRequest(diagnosticEvent.Value);
                    break;
                case requestStopEventName:
                    CompleteRequest(diagnosticEvent.Value, failure: null);
                    break;
                case requestExceptionEventName:
                    CompleteRequest(
                        diagnosticEvent.Value,
                        ReadProperty<Exception>(diagnosticEvent.Value, "Exception"));
                    break;
            }
        }
        catch
        {
            // Observability must never change application networking behavior.
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        lock (subscriptionsLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (var subscription in listenerSubscriptions)
            {
                subscription.Dispose();
            }
            listenerSubscriptions.Clear();
        }

        allListenersSubscription.Dispose();
        pendingRequests.Clear();
    }

    private void StartRequest(object? payload)
    {
        var request = ReadProperty<HttpRequestMessage>(payload, "Request");
        if (request is null
            || !shouldCapture()
            || request.RequestUri?.Scheme is not ("http" or "https")
            || IsWebSocketHandshake(request)
            || AnsightHttpMessageHandler.IsInternalTraffic(request)
            || request.Options.TryGetValue(AnsightHttpMessageHandler.ExplicitCaptureMarker, out var explicitCapture)
               && explicitCapture)
        {
            return;
        }

        pendingRequests[request] = new PendingRequest(
            DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp());
    }

    private static bool IsWebSocketHandshake(HttpRequestMessage request)
        => request.Headers.Contains("Sec-WebSocket-Key")
           || request.Headers.TryGetValues("Upgrade", out var upgradeValues)
              && upgradeValues.Any(value => string.Equals(
                  value,
                  "websocket",
                  StringComparison.OrdinalIgnoreCase));

    private void CompleteRequest(object? payload, Exception? failure)
    {
        var request = ReadProperty<HttpRequestMessage>(payload, "Request");
        if (request is null || !pendingRequests.TryRemove(request, out var pending))
        {
            return;
        }

        if (!shouldCapture())
        {
            return;
        }

        var response = ReadProperty<HttpResponseMessage>(payload, "Response");
        var completedAtUtc = DateTimeOffset.UtcNow;
        var record = NetworkRequestSanitizer.Sanitize(
            new NetworkRequestRecord
            {
                Id = Guid.CreateVersion7().ToString("N"),
                Source = "dotnet.httpclient.automatic",
                StartedAtUtc = pending.StartedAtUtc,
                CompletedAtUtc = completedAtUtc,
                DurationMilliseconds = Stopwatch.GetElapsedTime(pending.StartedTimestamp).TotalMilliseconds,
                Method = request.Method.Method,
                Url = request.RequestUri?.ToString() ?? "<unknown>",
                Protocol = response?.Version.ToString(),
                RequestHeaders = CaptureHeaders(request.Headers, request.Content?.Headers),
                RequestBodySizeBytes = request.Content?.Headers.ContentLength,
                StatusCode = response is null ? null : (int)response.StatusCode,
                ReasonPhrase = response?.ReasonPhrase,
                ResponseHeaders = CaptureHeaders(response?.Headers, response?.Content?.Headers),
                ResponseBodySizeBytes = response?.Content?.Headers.ContentLength,
                ErrorType = failure?.GetType().FullName,
                ErrorMessage = failure?.Message
            },
            sanitizationOptions);
        if (record is not null)
        {
            recordRequest(record);
        }
    }

    private static T? ReadProperty<T>(object? payload, string propertyName)
        where T : class
    {
        if (payload is null)
        {
            return null;
        }

        var property = payload.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        return property?.GetValue(payload) as T;
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

        return headers;
    }

    private sealed record PendingRequest(
        DateTimeOffset StartedAtUtc,
        long StartedTimestamp);
}
