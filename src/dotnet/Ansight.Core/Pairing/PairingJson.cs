using System.Text.Json;

namespace Ansight.Pairing;

/// <summary>
/// Shared JSON serializer settings used by the pairing protocol models and payload helpers.
/// </summary>
public static class PairingJson
{
    /// <summary>
    /// Maximum supported JSON depth for protocol envelopes and tool payloads. Visual trees can
    /// legitimately contain 64 nested UI nodes, with child arrays and envelope objects adding
    /// additional JSON levels around them.
    /// </summary>
    public const int MaximumDepth = 256;

    /// <summary>
    /// Compact camel-case JSON settings used for on-the-wire pairing payloads.
    /// </summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = MaximumDepth
    };

    /// <summary>
    /// Indented JSON settings based on <see cref="Compact"/> for stored files and debugging output.
    /// </summary>
    public static readonly JsonSerializerOptions Pretty = new(Compact)
    {
        WriteIndented = true
    };
}
