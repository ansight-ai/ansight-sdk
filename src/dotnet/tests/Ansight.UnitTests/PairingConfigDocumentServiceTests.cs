using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingConfigDocumentServiceTests
{
    [Fact]
    public void TryParseDocument_ParsesEnrollmentInvite()
    {
        var invite = PairingTestDocumentFactory.CreateEnrollmentInvite(
            configId: "invite-document");
        var json = PairingTestDocumentFactory.CreateConfigDocumentJson(
            invite,
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "127.0.0.1",
                source: "unit-test"));

        var success = new PairingConfigDocumentService()
            .TryParseDocument(json, out var document, out var error);

        Assert.True(success, error);
        Assert.NotNull(document);
        Assert.Equal("invite-document", document!.Config.ConfigId);
        Assert.False(string.IsNullOrWhiteSpace(document.Config.Enrollment!.Secret));
        Assert.Equal(["127.0.0.1"], document.DiscoveryHint!.HostAddresses!);
    }

    [Fact]
    public void TryParseDocument_RejectsUnknownSchema()
    {
        const string json = """
                            {
                              "schema": "example.unknown",
                              "inviteId": "unknown"
                            }
                            """;

        var success = new PairingConfigDocumentService()
            .TryParseDocument(json, out var document, out var error);

        Assert.False(success);
        Assert.Null(document);
        Assert.Contains("Unsupported enrollment invite schema", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateDocument_ReturnsFalseWhenExpectedAppIdDoesNotMatch()
    {
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateEnrollmentInvite(appId: "com.ansight.actual")
        };

        var success = new PairingConfigDocumentService()
            .TryValidateDocument(document, "com.ansight.expected", out var error);

        Assert.False(success);
        Assert.Contains("does not match expected app id", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateDocument_AcceptsGenericInviteForRuntimeApp()
    {
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateEnrollmentInvite(
                appId: PairingConfig.AnyAppId,
                appName: "Any Ansight app")
        };

        var success = new PairingConfigDocumentService()
            .TryValidateDocument(document, "com.ansight.actual", out var error);

        Assert.True(success, error);
    }

    [Fact]
    public void TryValidateConfig_AcceptsCurrentEnrollmentInvite()
    {
        var invite = PairingTestDocumentFactory.CreateEnrollmentInvite(appId: "com.ansight.test");

        var success = new PairingConfigDocumentService()
            .TryValidateConfig(invite, "com.ansight.test", out var error);

        Assert.True(success, error);
        Assert.Equal(["ws"], invite.AllowedTransports);
        Assert.NotNull(invite.Enrollment);
    }

    [Fact]
    public void TryValidateConfig_AllowsReconnectAfterQrExpiry()
    {
        var invite = PairingTestDocumentFactory.CreateEnrollmentInvite(
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            registrationExpiresAt: DateTimeOffset.UtcNow.AddDays(1));

        var success = new PairingConfigDocumentService()
            .TryValidateConfig(invite, invite.AppId, out var error);

        Assert.True(success, error);
    }

    [Fact]
    public void TryValidateConfig_RejectsExpiredRegistration()
    {
        var invite = PairingTestDocumentFactory.CreateEnrollmentInvite(
            registrationExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var success = new PairingConfigDocumentService()
            .TryValidateConfig(invite, invite.AppId, out var error);

        Assert.False(success);
        Assert.Contains("registration expired", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParseDocument_WhenInvitePayloadIsProvided_ReturnsInvite()
    {
        var invite = PairingTestDocumentFactory.CreateEnrollmentInvite(configId: "invite-direct");
        var json = PairingConfigJson.Serialize(invite);

        var success = new PairingConfigDocumentService()
            .TryParseDocument(json, out var document, out var error);

        Assert.True(success, error);
        Assert.Equal("invite-direct", document!.Config.ConfigId);
        Assert.Null(document.DiscoveryHint);
    }

    [Fact]
    public void TryParseConfigDocument_WhenInvitePayloadIsProvided_WrapsInvite()
    {
        var invite = PairingTestDocumentFactory.CreateEnrollmentInvite(configId: "invite-wrap");
        var json = PairingConfigJson.Serialize(invite);

        var success = new PairingConfigDocumentService()
            .TryParseConfigDocument(json, out var document, out var error);

        Assert.True(success, error);
        Assert.Equal(PairingConfigDocument.SchemaName, document!.Schema);
        Assert.Equal("invite-wrap", document.Config.ConfigId);
    }
}
