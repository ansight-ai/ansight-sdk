namespace Ansight.Tools.Database;

public static class DatabaseToolSecurityProfiles
{
    public static ToolSecurity ListDatabases { get; } = new(
        ToolSecurityLevel.Moderate,
        "Reveals database names and storage locations inside the app sandbox.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.AccessesDatabases);

    public static ToolSecurity DescribeSchema { get; } = new(
        ToolSecurityLevel.Moderate,
        "Reveals database structure, including table and column metadata.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.AccessesDatabases);

    public static ToolSecurity Query { get; } = new(
        ToolSecurityLevel.High,
        "Reads and exports structured data from an app database.",
        ToolSecurityImplications.ReadsAppData,
        ToolSecurityImplications.ExportsData,
        ToolSecurityImplications.AccessesDatabases);
}
