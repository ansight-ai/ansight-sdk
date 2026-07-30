using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Ansight.Pairing.Models;

namespace Ansight.Pairing;

/// <summary>
/// Encodes and decodes a compact QR-friendly representation of a <see cref="PairingConfigDocument"/>.
/// </summary>
public static class PairingConfigCodeGenerator
{
    /// <summary>
    /// Prefix used by compact pairing config codes.
    /// </summary>
    public const string FormatPrefix = "ans2";

    /// <summary>
    /// Serializes a pairing config document into a compact QR-friendly code.
    /// </summary>
    public static string Serialize(PairingConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var json = PairingConfigDocumentJson.Serialize(document, indented: false);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        using var compressedStream = new MemoryStream();
        using (var gzip = new GZipStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(jsonBytes, 0, jsonBytes.Length);
        }

        return $"{FormatPrefix}:{PairingCrypto.ToBase64Url(compressedStream.ToArray())}";
    }

    /// <summary>
    /// Attempts to parse a compact pairing config code.
    /// </summary>
    public static bool TryParse(string payload, out PairingConfigDocument? document)
    {
        document = null;
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
            document = JsonSerializer.Deserialize<PairingConfigDocument>(json, PairingJson.Compact);
            if (document?.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(document.Discovery);
            }

            return document is not null
                   && string.Equals(
                       document.Schema,
                       PairingConfigDocument.SchemaName,
                       StringComparison.Ordinal);
        }
        catch
        {
            document = null;
            return false;
        }
    }
}
