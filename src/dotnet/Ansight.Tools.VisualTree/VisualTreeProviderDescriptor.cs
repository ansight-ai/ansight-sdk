namespace Ansight.Tools.VisualTree;

/// <summary>
/// Describes a registered visual-tree capture source.
/// </summary>
/// <param name="Source">Stable source identifier.</param>
/// <param name="DisplayName">Human-readable provider name.</param>
public sealed record VisualTreeProviderDescriptor(string Source, string DisplayName);
