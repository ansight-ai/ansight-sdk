using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

public sealed class HostSessionJpegCapturePolicyTests
{
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
