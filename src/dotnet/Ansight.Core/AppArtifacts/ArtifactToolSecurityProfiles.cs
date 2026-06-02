namespace Ansight.Artifacts;

using Ansight.Tools;

internal static class ArtifactToolSecurityProfiles
{
    internal static ToolSecurity Query { get; } = new(
        ToolSecurityLevel.Moderate,
        "Discovers app-provided artifact definitions and descriptive metadata.",
        ToolSecurityImplications.MetadataDisclosure);

    internal static ToolSecurity Request { get; } = new(
        ToolSecurityLevel.High,
        "Requests and exports an app-provided artifact snapshot.",
        ToolSecurityImplications.ExportsData,
        ToolSecurityImplications.UsesBinaryTransfer);
}
