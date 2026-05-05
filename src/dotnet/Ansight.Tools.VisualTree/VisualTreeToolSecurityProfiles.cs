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

    public static ToolSecurity ShowOverlay { get; } = new(
        ToolSecurityLevel.Critical,
        "Adds an input-transparent diagnostic overlay to the live app window.",
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.MutatesRuntimeState);

    public static ToolSecurity GetOverlay { get; } = new(
        ToolSecurityLevel.High,
        "Reveals metadata and geometry for a live diagnostic UI overlay.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi);

    public static ToolSecurity QueryOverlays { get; } = new(
        ToolSecurityLevel.High,
        "Lists live diagnostic UI overlays and their attached metadata.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi);

    public static ToolSecurity UpdateOverlay { get; } = new(
        ToolSecurityLevel.Critical,
        "Edits an existing diagnostic overlay in the live app window.",
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.MutatesRuntimeState);

    public static ToolSecurity RemoveOverlay { get; } = new(
        ToolSecurityLevel.Critical,
        "Removes a diagnostic overlay from the live app window.",
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.MutatesRuntimeState);

    public static ToolSecurity ClearOverlays { get; } = new(
        ToolSecurityLevel.Critical,
        "Removes live diagnostic overlays from the app window.",
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.MutatesRuntimeState);
}
