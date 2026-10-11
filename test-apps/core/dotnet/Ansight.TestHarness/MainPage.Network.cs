using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Ansight.TestHarness;

public partial class MainPage
{
    private CancellationTokenSource? networkProbeCancellation;

    private async void OnNetworkProbeClicked(object? sender, EventArgs args)
    {
        if (networkProbeCancellation is not null) return;
        if (!Uri.TryCreate(NetworkEndpointEntry.Text, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            NetworkProbeStatus.Text = "Invalid endpoint";
            return;
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        networkProbeCancellation = cancellation;
        NetworkProbeStatus.Text = "Running";
        NetworkProbeDetails.Text = "";
        var watch = Stopwatch.StartNew();
        long bytes = 0;
        var outcome = "Failed";
        int? statusCode = null;
        string? failure = null;
        try
        {
            // A new in-process connection per probe makes conditioning and recovery
            // observable; HTTP/1.1 deliberately avoids the unshaped QUIC path.
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
            using var client = new HttpClient(handler);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            statusCode = (int)response.StatusCode;
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellation.Token)) != 0)
            {
                bytes += read;
                if (bytes > 8 * 1024 * 1024) throw new IOException("Probe response exceeds 8 MiB.");
            }
            outcome = "Succeeded";
        }
        catch (OperationCanceledException) { outcome = "Cancelled or timed out"; }
        catch (Exception error) when (error is HttpRequestException or IOException)
        { failure = error.GetType().Name; }
        finally
        {
            watch.Stop();
            networkProbeCancellation = null;
            NetworkProbeStatus.Text = outcome;
            NetworkProbeDetails.Text = $"{bytes:N0} bytes • {watch.ElapsedMilliseconds} ms • HTTP {statusCode?.ToString() ?? "none"}";
            Runtime.Event("network.probe.completed", AppEventType.Info, JsonSerializer.Serialize(new
            {
                source = "harness", outcome, bytes, elapsedMilliseconds = watch.ElapsedMilliseconds,
                statusCode, failure, transport = "tcp", protocol = "http/1.1"
            }));
        }
    }

    private void OnCancelNetworkProbeClicked(object? sender, EventArgs args) => networkProbeCancellation?.Cancel();
}
