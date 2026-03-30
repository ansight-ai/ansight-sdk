namespace Ansight.Pairing.Models;

/// <summary>
/// Primary runtime engine metadata for the connected app.
/// </summary>
public sealed class DeviceRuntimeEngineProfile
{
    /// <summary>
    /// Engine name, such as <c>dotnet</c> or a JavaScript engine.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Engine version string.
    /// </summary>
    public string? Version { get; set; }
}
