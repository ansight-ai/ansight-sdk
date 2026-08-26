using Ansight.Network;

namespace Ansight.UnitTests;

public sealed class AutomaticHttpClientNetworkCaptureTests
{
    [Fact]
    public void OptionsBuilder_AutomaticCaptureIsExplicitAndCloneable()
    {
        var defaults = Options.CreateBuilder().Build();
        var enabled = Options.CreateBuilder()
            .WithNetworkCapture(builder => builder.WithoutRequestBodies())
            .Build();
        var clone = Options.CreateBuilder(enabled).Build();
        var disabled = Options.CreateBuilder(enabled)
            .WithoutNetworkCapture()
            .Build();

        Assert.Null(defaults.AutomaticNetworkCapture);
        Assert.NotNull(enabled.AutomaticNetworkCapture);
        Assert.False(enabled.AutomaticNetworkCapture.CaptureRequestBody);
        Assert.NotSame(enabled.AutomaticNetworkCapture, clone.AutomaticNetworkCapture);
        Assert.False(clone.AutomaticNetworkCapture!.CaptureRequestBody);
        Assert.Null(disabled.AutomaticNetworkCapture);
    }

    [Fact]
    public void DiagnosticEvents_RecordPlatformBackedHttpClientMetadata()
    {
        var records = new List<NetworkRequestRecord>();
        using var capture = new AutomaticHttpClientNetworkCapture(
            () => true,
            records.Add,
            new NetworkRequestSanitizationOptions());
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://example.test/items?access_token=secret");
        request.Headers.Add("Authorization", "Bearer secret");
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
        {
            RequestMessage = request,
            ReasonPhrase = "Accepted"
        };

        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Start",
            new { Request = request }));
        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Stop",
            new { Request = request, Response = response }));

        var record = Assert.Single(records);
        Assert.Equal("dotnet.httpclient.automatic", record.Source);
        Assert.Equal("GET", record.Method);
        Assert.Equal(202, record.StatusCode);
        Assert.Contains("%3Credacted%3E", record.Url);
        Assert.Contains(
            record.RequestHeaders,
            header => header.Name == "Authorization" && header.Value == "<redacted>");
        Assert.Null(record.RequestBody);
        Assert.Null(record.ResponseBody);
    }

    [Fact]
    public void ExplicitHandlerMarker_PreventsDuplicateCapture()
    {
        var records = new List<NetworkRequestRecord>();
        using var capture = new AutomaticHttpClientNetworkCapture(
            () => true,
            records.Add,
            new NetworkRequestSanitizationOptions());
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        request.Options.Set(AnsightHttpMessageHandler.ExplicitCaptureMarker, true);

        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Start",
            new { Request = request }));
        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Stop",
            new { Request = request, Response = new HttpResponseMessage() }));

        Assert.Empty(records);
    }

    [Theory]
    [InlineData("wss://127.0.0.1:45124/ws")]
    [InlineData("ws://127.0.0.1:45124/ws")]
    public void WebSocketChannels_AreNeverCaptured(string url)
    {
        var records = new List<NetworkRequestRecord>();
        using var capture = new AutomaticHttpClientNetworkCapture(
            () => true,
            records.Add,
            new NetworkRequestSanitizationOptions());
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Start",
            new { Request = request }));
        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Stop",
            new { Request = request, Response = new HttpResponseMessage() }));

        Assert.Empty(records);
    }

    [Fact]
    public void AnsightInternalHttpTraffic_IsNeverCaptured()
    {
        var records = new List<NetworkRequestRecord>();
        using var capture = new AutomaticHttpClientNetworkCapture(
            () => true,
            records.Add,
            new NetworkRequestSanitizationOptions());
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/ansight-upload");
        AnsightHttpMessageHandler.MarkAsInternalTraffic(request);

        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Start",
            new { Request = request }));
        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Stop",
            new { Request = request, Response = new HttpResponseMessage() }));

        Assert.Empty(records);
    }

    [Fact]
    public void AnsightInternalHttpTrafficHeader_IsNeverCaptured()
    {
        var records = new List<NetworkRequestRecord>();
        using var capture = new AutomaticHttpClientNetworkCapture(
            () => true,
            records.Add,
            new NetworkRequestSanitizationOptions());
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/ansight-upload");
        request.Headers.TryAddWithoutValidation(AnsightHttpMessageHandler.InternalTrafficHeaderName, "1");

        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Start",
            new { Request = request }));
        capture.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Stop",
            new { Request = request, Response = new HttpResponseMessage() }));

        Assert.Empty(records);
    }
}
