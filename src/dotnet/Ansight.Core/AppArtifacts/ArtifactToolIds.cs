namespace Ansight.Artifacts;

/// <summary>
/// Tool ids for core artifact discovery and request tools.
/// </summary>
public static class ArtifactToolIds
{
    /// <summary>
    /// Queries artifact providers and their currently available artifacts.
    /// </summary>
    public const string Query = "artifacts.query";

    /// <summary>
    /// Requests a provider to create and stream an artifact snapshot.
    /// </summary>
    public const string Request = "artifacts.request";
}
