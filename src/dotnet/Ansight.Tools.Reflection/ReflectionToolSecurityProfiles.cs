namespace Ansight.Tools.Reflection;

public static class ReflectionToolSecurityProfiles
{
    public static ToolSecurity ListRoots { get; } = new(
        ToolSecurityLevel.High,
        "Reveals registered runtime object roots, metadata, and live availability details.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsRuntimeState);

    public static ToolSecurity InspectObject { get; } = new(
        ToolSecurityLevel.Critical,
        "Reads live runtime object state, including in-memory values and non-public members when configured.",
        ToolSecurityImplications.ReadsAppData,
        ToolSecurityImplications.InspectsRuntimeState);

    public static ToolSecurity DescribeType { get; } = new(
        ToolSecurityLevel.Moderate,
        "Describes runtime type metadata without reading live object values.",
        ToolSecurityImplications.MetadataDisclosure);

    public static ToolSecurity SetMemberValue { get; } = new(
        ToolSecurityLevel.Critical,
        "Mutates live runtime object state through fields and properties reachable from registered roots.",
        ToolSecurityImplications.WritesAppData,
        ToolSecurityImplications.MutatesRuntimeState);

    public static ToolSecurity InvokeMethod { get; } = new(
        ToolSecurityLevel.Critical,
        "Invokes application methods reachable from registered roots.",
        ToolSecurityImplications.WritesAppData,
        ToolSecurityImplications.InvokesAppCode,
        ToolSecurityImplications.MutatesRuntimeState);
}
