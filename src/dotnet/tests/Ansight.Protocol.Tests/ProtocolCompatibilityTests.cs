using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.Pairing;
using Ansight.Pairing.Models;
using Ansight.Tools;
using Xunit;

namespace Ansight.Protocol.Tests;

public sealed class ProtocolCompatibilityTests
{
    [Fact]
    public void ProtocolAssemblyContainsNoSdkRuntimeOrNativeDependency()
    {
        var references = typeof(ToolProtocolEnvelope).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name?.StartsWith("Ansight.", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(references, reference => reference.Name?.StartsWith("Microsoft.Build", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ExistingEnrollmentInviteKeepsItsWireNames()
    {
        const string json = """
            {"schema":"ansight.enrollment-invite-document.v2","invite":{"schema":"ansight.enrollment-invite.v2","inviteId":"invite_123","appId":"*","appName":"Any Ansight app","issuedAt":"2026-07-30T01:00:00Z","expiresAt":"2026-07-30T01:10:00Z","minProtocolVersion":2,"allowedTransports":["ws"],"host":{"hostId":"host_123","hostName":"Developer Mac","discoveryPort":45123},"enrollment":{"accessToken":"synthetic-one-use-token","expiresAt":"2026-07-30T01:10:00Z","grantExpiresAt":"2026-08-13T01:00:00Z","maxUses":1,"maxToolPolicy":"read"}}}
            """;
        var invite = JsonSerializer.Deserialize<PairingConfigDocument>(json, PairingJson.Compact)!;
        Assert.Equal("invite_123", invite.Config.ConfigId);
        Assert.NotNull(invite.Config.Enrollment);
        Assert.Equal("synthetic-one-use-token", invite.Config.Enrollment.Secret);
        var encoded = JsonSerializer.SerializeToNode(invite, PairingJson.Compact)!;
        Assert.Equal("invite_123", encoded["invite"]!["inviteId"]!.GetValue<string>());
        Assert.Equal("synthetic-one-use-token", encoded["invite"]!["enrollment"]!["accessToken"]!.GetValue<string>());
        Assert.Null(encoded["config"]);
        Assert.Null(encoded["invite"]!["configId"]);
    }

    [Fact]
    public void ExistingToolResultKeepsItsEnvelopeAndPayload()
    {
        const string json = """
            {"type":"tool.result","id":"request_123","replyTo":null,"sessionId":"session_123","sentAt":"2026-07-30T01:00:00+00:00","capability":"tool.exec","payload":{"text":"example","success":true}}
            """;
        var envelope = JsonSerializer.Deserialize<ToolProtocolEnvelope>(json, PairingJson.Compact)!;
        Assert.Equal(ToolProtocolMessageTypes.ResultType, envelope.Type);
        Assert.Equal("tool.exec", envelope.Capability);
        var actual = JsonSerializer.SerializeToNode(envelope, PairingJson.Compact);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), actual), actual?.ToJsonString());
    }

    [Fact]
    public void CompressedToolPayloadKeepsItsPublishedEncodingAndRoundTrips()
    {
        var payload = new JsonObject { ["text"] = new string('x', 100_000), ["success"] = true };
        var encoded = ToolProtocolPayloadEncoding.EncodeIfBeneficial(payload, PairingJson.Compact);
        Assert.Equal("gzip-base64-json", encoded!["$ansightEncoding"]!.GetValue<string>());
        Assert.True(ToolProtocolPayloadEncoding.TryDecode(encoded, out var decoded, out var error), error);
        Assert.True(JsonNode.DeepEquals(payload, decoded));
    }
}
