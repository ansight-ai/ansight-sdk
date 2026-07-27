using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

public sealed class HostSessionJpegCapturePolicyTests
{
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
