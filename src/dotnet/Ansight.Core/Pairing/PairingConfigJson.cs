using System.Text.Json;
using Ansight.Pairing.Models;

namespace Ansight.Pairing;

internal static class PairingConfigJson
{
    public static string Serialize(PairingConfig invite, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return JsonSerializer.Serialize(
            CreateJsonModel(invite),
            indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    public static object CreateJsonModel(PairingConfig invite)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return new EnrollmentInviteJsonModel
        {
            Schema = invite.Schema,
            InviteId = invite.ConfigId,
            AppId = invite.AppId,
            AppName = invite.AppName,
            IssuedAt = invite.IssuedAt,
            ExpiresAt = invite.ExpiresAt,
            MinProtocolVersion = invite.MinProtocolVersion,
            AllowedTransports = invite.AllowedTransports,
            Host = invite.Host,
            Enrollment = invite.Enrollment
                         ?? throw new InvalidOperationException(
                             "Enrollment invite is missing its access token.")
        };
    }

    private sealed class EnrollmentInviteJsonModel
    {
        public required string Schema { get; init; }
        public required string InviteId { get; init; }
        public required string AppId { get; init; }
        public required string AppName { get; init; }
        public required DateTimeOffset IssuedAt { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public required int MinProtocolVersion { get; init; }
        public required string[] AllowedTransports { get; init; }
        public required PairingHost Host { get; init; }
        public required PairingEnrollment Enrollment { get; init; }
    }
}
