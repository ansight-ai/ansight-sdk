namespace Ansight;

/// <summary>
/// Optional SDK feature initialized alongside the core runtime by a separate package.
/// </summary>
public interface IRuntimeFeature
{
    /// <summary>
    /// Stable, case-insensitive feature identifier.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Initializes the feature for the newly created runtime.
    /// Implementations should keep failures isolated from core runtime startup.
    /// </summary>
    void Initialize(IRuntime runtime);
}
