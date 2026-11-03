using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

public sealed class HostSessionJpegCapturePolicyTests
{
    [Fact]
    public void PeriodicControlPausesAndResumesAndIgnoresMalformedRequests()
    {
        using var transport = new Ansight.Pairing.PairingSessionTransport();
        foreach (var paused in new[] { true, false })
        {
            var request = new JsonObject
            {
                ["type"] = "session.screenshots", ["id"] = "policy",
                ["payload"] = new JsonObject { ["paused"] = paused }
            };
            Assert.True(transport.TryHandlePeriodicScreenshotControl(request.ToJsonString(), out var response));
            Assert.Equal(paused, transport.PeriodicScreenshotsPaused);
            Assert.Equal("policy", JsonNode.Parse(response!)!["replyTo"]!.GetValue<string>());
        }
        Assert.False(transport.TryHandlePeriodicScreenshotControl("{\"type\":\"session.screenshots\",\"payload\":{\"paused\":\"true\"}}", out _));
        Assert.False(transport.PeriodicScreenshotsPaused);
    }

    [Fact]
    public void AddClientCapabilities_AdvertisesConfiguredMaximumWidth()
    {
        var payload = new JsonObject();

        HostSessionJpegCapturePolicy.AddClientCapabilities(
            payload,
            new SessionJpegCaptureOptions
            {
                MaxWidth = 1_024
            });

        Assert.Equal(
            HostSessionJpegCapturePolicy.ControlVersion,
            payload[HostSessionJpegCapturePolicy.ControlVersionPropertyName]?.GetValue<int>());
        var capture = Assert.IsType<JsonObject>(payload["sessionJpegCapture"]);
        Assert.Equal(1_024, capture["maxWidth"]?.GetValue<int>());
    }

    [Fact]
    public void AddClientCapabilities_PreservesNativeWidthRequest()
    {
        var payload = new JsonObject();

        HostSessionJpegCapturePolicy.AddClientCapabilities(
            payload,
            new SessionJpegCaptureOptions
            {
                MaxWidth = null
            });

        var capture = Assert.IsType<JsonObject>(payload["sessionJpegCapture"]);
        Assert.True(capture.ContainsKey("maxWidth"));
        Assert.Null(capture["maxWidth"]);
    }

    [Fact]
    public void AddClientCapabilities_OmitsCaptureRequestWhenCaptureIsNotConfigured()
    {
        var payload = new JsonObject();

        HostSessionJpegCapturePolicy.AddClientCapabilities(payload, null);

        Assert.Equal(
            HostSessionJpegCapturePolicy.ControlVersion,
            payload[HostSessionJpegCapturePolicy.ControlVersionPropertyName]?.GetValue<int>());
        Assert.False(payload.ContainsKey("sessionJpegCapture"));
    }

    [Fact]
    public void FromPayload_HostModeDisablesSdkCapture()
    {
        var payload = new JsonObject
        {
            ["sessionJpegCapture"] = new JsonObject
            {
                ["mode"] = "host",
                ["source"] = "simctl"
            }
        };

        var policy = HostSessionJpegCapturePolicy.FromPayload(payload);

        Assert.True(policy.UseHostCapture);
        Assert.Equal("simctl", policy.Source);
    }

    [Fact]
    public void FromPayload_MissingOrAppModeKeepsSdkCapture()
    {
        Assert.False(HostSessionJpegCapturePolicy.FromPayload(null).UseHostCapture);
        Assert.False(HostSessionJpegCapturePolicy.FromPayload(
            new JsonObject
            {
                ["sessionJpegCapture"] = new JsonObject
                {
                    ["mode"] = "app"
                }
            }).UseHostCapture);
    }
}
