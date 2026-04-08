using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Ansight.Pairing.Models;

namespace Ansight.Pairing;

/// <summary>
/// Encodes and decodes a compact QR-friendly representation of a <see cref="PairingTicket"/>.
/// </summary>
public static class PairingTicketCodeGenerator
{
    /// <summary>
    /// Prefix used by compact pairing ticket codes.
    /// </summary>
    public const string FormatPrefix = "apt1";

    /// <summary>
    /// Serializes a pairing ticket into a compact QR-friendly code.
    /// </summary>
    public static string Serialize(PairingTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var json = PairingTicketJson.Serialize(ticket, indented: false);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        using var compressedStream = new MemoryStream();
        using (var gzip = new GZipStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(jsonBytes, 0, jsonBytes.Length);
        }

        return $"{FormatPrefix}:{PairingCrypto.ToBase64Url(compressedStream.ToArray())}";
    }

    /// <summary>
    /// Attempts to parse a compact pairing ticket code.
    /// </summary>
    public static bool TryParse(string payload, out PairingTicket? ticket)
    {
        ticket = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var normalizedPayload = payload.Trim();
        if (!normalizedPayload.StartsWith($"{FormatPrefix}:", StringComparison.Ordinal))
        {
            return false;
        }

        var encodedPayload = normalizedPayload[(FormatPrefix.Length + 1)..];
        byte[] compressedBytes;
        try
        {
            compressedBytes = PairingCrypto.FromBase64Url(encodedPayload);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            using var compressedStream = new MemoryStream(compressedBytes);
            using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var jsonStream = new MemoryStream();
            gzip.CopyTo(jsonStream);
            var json = Encoding.UTF8.GetString(jsonStream.ToArray());
            ticket = JsonSerializer.Deserialize<PairingTicket>(json, PairingJson.Compact);
            if (ticket?.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(ticket.Discovery);
            }

            return ticket is not null &&
                   string.Equals(ticket.Schema, PairingTicket.SchemaName, StringComparison.Ordinal);
        }
        catch
        {
            ticket = null;
            return false;
        }
    }
}
