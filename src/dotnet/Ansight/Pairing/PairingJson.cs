using System.Text.Json;

namespace Ansight.Pairing;

/// <summary>
/// Shared JSON serializer settings used by the pairing protocol models and payload helpers.
/// </summary>
public static class PairingJson
{
    /// <summary>
    /// Compact camel-case JSON settings used for on-the-wire pairing payloads.
    /// </summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Indented JSON settings based on <see cref="Compact"/> for stored files and debugging output.
    /// </summary>
    public static readonly JsonSerializerOptions Pretty = new(Compact)
    {
        WriteIndented = true
    };
}
