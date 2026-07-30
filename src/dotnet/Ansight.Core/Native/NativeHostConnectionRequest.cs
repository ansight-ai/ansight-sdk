namespace Ansight.Native;

internal sealed record NativeHostConnectionRequest(
    HostConnectionRequestKind Kind,
    string? Payload = null,
    string? ClientName = null,
    string? ExpectedAppId = null,
    string? HostAddressOverride = null);
