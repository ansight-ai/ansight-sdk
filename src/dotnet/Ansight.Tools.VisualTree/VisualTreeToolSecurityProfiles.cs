namespace Ansight.Tools.VisualTree;

public static class VisualTreeToolSecurityProfiles
{
    public static ToolSecurity GetVisualTree { get; } = new(
        ToolSecurityLevel.High,
        "Reveals the live UI hierarchy, including labels and layout metadata.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi);

    public static ToolSecurity GetScreenshot { get; } = new(
        ToolSecurityLevel.High,
        "Captures and exports the current app UI as an image.",
        ToolSecurityImplications.ExportsData,
        ToolSecurityImplications.CapturesScreenshots,
        ToolSecurityImplications.UsesBinaryTransfer,
        ToolSecurityImplications.InspectsUi);

    public static ToolSecurity InspectNode { get; } = new(
        ToolSecurityLevel.High,
        "Reveals detailed metadata for a specific UI node.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi);
}
