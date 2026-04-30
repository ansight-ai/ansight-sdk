namespace Ansight.Tools;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Encodes large tool protocol payloads so they take less space on the live WebSocket.
/// </summary>
public static class ToolProtocolPayloadEncoding
{
    private const string EncodingPropertyName = "$ansightEncoding";
    private const string GzipBase64JsonEncoding = "gzip-base64-json";
    private const int DefaultCompressionThresholdBytes = 32 * 1024;

    /// <summary>
    /// Compresses a payload when its compact JSON representation is large enough and compression is beneficial.
    /// </summary>
    public static JsonNode? EncodeIfBeneficial(JsonNode? payload, JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(jsonOptions);

        if (payload is null)
        {
            return null;
        }

        var sourceBytes = JsonSerializer.SerializeToUtf8Bytes(payload, jsonOptions);
        if (sourceBytes.Length < DefaultCompressionThresholdBytes)
        {
            return payload;
        }

        var compressedBytes = Compress(sourceBytes);
        var encodedBytes = Encoding.ASCII.GetByteCount(Convert.ToBase64String(compressedBytes));
        if (encodedBytes >= sourceBytes.Length)
        {
            return payload;
        }

        return new JsonObject
        {
            [EncodingPropertyName] = GzipBase64JsonEncoding,
            ["contentType"] = "application/json",
            ["originalByteCount"] = sourceBytes.Length,
            ["compressedByteCount"] = compressedBytes.Length,
            ["data"] = Convert.ToBase64String(compressedBytes)
        };
    }

    /// <summary>
    /// Decodes a payload produced by <see cref="EncodeIfBeneficial"/>. Unencoded payloads pass through unchanged.
    /// </summary>
    public static bool TryDecode(JsonNode? payload, out JsonNode? decodedPayload, out string error)
    {
        decodedPayload = payload;
        error = string.Empty;

        if (payload is not JsonObject payloadObject)
        {
            return true;
        }

        var encoding = payloadObject[EncodingPropertyName]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return true;
        }

        if (!string.Equals(encoding, GzipBase64JsonEncoding, StringComparison.Ordinal))
        {
            error = $"Unsupported tool payload encoding '{encoding}'.";
            return false;
        }

        var encodedData = payloadObject["data"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(encodedData))
        {
            error = "Encoded tool payload is missing data.";
            return false;
        }

        try
        {
            var compressedBytes = Convert.FromBase64String(encodedData);
            var json = Encoding.UTF8.GetString(Decompress(compressedBytes));
            decodedPayload = JsonNode.Parse(json);
            return true;
        }
        catch (Exception exception)
        {
            error = $"Failed to decode compressed tool payload: {exception.Message}";
            decodedPayload = payload;
            return false;
        }
    }

    private static byte[] Compress(byte[] sourceBytes)
    {
        using var compressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzipStream.Write(sourceBytes, 0, sourceBytes.Length);
        }

        return compressedStream.ToArray();
    }

    private static byte[] Decompress(byte[] compressedBytes)
    {
        using var compressedStream = new MemoryStream(compressedBytes);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        gzipStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }
}
