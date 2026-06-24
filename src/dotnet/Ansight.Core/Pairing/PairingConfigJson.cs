using System.Text.Json;
using Ansight.Pairing.Models;

namespace Ansight.Pairing;

internal static class PairingConfigJson
{
    public static string Serialize(PairingConfig config, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(config);

        return JsonSerializer.Serialize(
            CreateJsonModel(config),
            indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    public static object CreateJsonModel(PairingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new PairingConfigJsonModel
        {
            Schema = config.Schema,
            ConfigId = config.ConfigId,
            AppId = config.AppId,
            AppName = config.AppName,
            IssuedAt = config.IssuedAt,
            ExpiresAt = config.ExpiresAt,
            OneTimeToken = config.OneTimeToken,
            Host = new PairingHostJsonModel
            {
                HostPubKey = config.Host.HostPubKey,
                HostPubKeyFingerprint = config.Host.HostPubKeyFingerprint
            },
            Challenge = config.Challenge,
            Signature = config.Signature
        };
    }

    private sealed class PairingConfigJsonModel
    {
        public required string Schema { get; init; }

        public required string ConfigId { get; init; }

        public required string AppId { get; init; }

        public required string AppName { get; init; }

        public required DateTimeOffset IssuedAt { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        public required string OneTimeToken { get; init; }

        public required PairingHostJsonModel Host { get; init; }

        public required PairingChallenge Challenge { get; init; }

        public required string Signature { get; init; }
    }

    private sealed class PairingHostJsonModel
    {
        public required string HostPubKey { get; init; }

        public required string HostPubKeyFingerprint { get; init; }
    }
}
