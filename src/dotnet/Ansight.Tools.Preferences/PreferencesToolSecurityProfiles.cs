namespace Ansight.Tools.Preferences;

public static class PreferencesToolSecurityProfiles
{
    public static ToolSecurity ListKeys { get; } = new(
        ToolSecurityLevel.Moderate,
        "Reveals preference key names and store metadata.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.AccessesPreferences);

    public static ToolSecurity GetValue { get; } = new(
        ToolSecurityLevel.High,
        "Reads preference values that may include app configuration or user state.",
        ToolSecurityImplications.ReadsAppData,
        ToolSecurityImplications.AccessesPreferences);

    public static ToolSecurity SetValue { get; } = new(
        ToolSecurityLevel.High,
        "Mutates preference values and can change app configuration or persisted state.",
        ToolSecurityImplications.WritesAppData,
        ToolSecurityImplications.AccessesPreferences);

    public static ToolSecurity RemoveKey { get; } = new(
        ToolSecurityLevel.High,
        "Deletes persisted preference values and can remove app configuration or state.",
        ToolSecurityImplications.DeletesAppData,
        ToolSecurityImplications.AccessesPreferences);
}
