using System.Text.Json;
using System.Text.Json.Serialization;
using Ansight.Pairing.Models;

namespace Ansight.Pairing;

public static class PairingTicketJson
{
    public static string Serialize(PairingTicket ticket, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var normalizedDiscovery = ticket.Discovery is null
            ? null
            : PairingDiscoveryHintHostAddresses.NormalizeInPlace(ticket.Discovery);

        var model = new PairingTicketJsonModel
        {
            Schema = PairingTicket.SchemaName,
            Config = PairingConfigJson.CreateJsonModel(ticket.Config),
            Discovery = normalizedDiscovery
        };

        return JsonSerializer.Serialize(model, indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    private sealed class PairingTicketJsonModel
    {
        public required string Schema { get; init; }

        public required object Config { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PairingDiscoveryHint? Discovery { get; init; }
    }
}
