namespace Ansight.Tools.FileSystem;

public static class FileSystemToolSecurityProfiles
{
    public static ToolSecurity ListDirectory { get; } = new(
        ToolSecurityLevel.Moderate,
        "Reveals file and directory names inside configured sandbox roots.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.AccessesFileSystem);

    public static ToolSecurity ReadFile { get; } = new(
        ToolSecurityLevel.High,
        "Reads and exports file contents from configured sandbox roots.",
        ToolSecurityImplications.ReadsAppData,
        ToolSecurityImplications.ExportsData,
        ToolSecurityImplications.AccessesFileSystem);

    public static ToolSecurity DownloadFile { get; } = new(
        ToolSecurityLevel.High,
        "Streams file contents out of the app sandbox in resumable chunks.",
        ToolSecurityImplications.ReadsAppData,
        ToolSecurityImplications.ExportsData,
        ToolSecurityImplications.AccessesFileSystem);

    public static ToolSecurity BeginBinaryDownload { get; } = new(
        ToolSecurityLevel.High,
        "Transfers sandboxed file contents over the pairing channel as binary frames.",
        ToolSecurityImplications.ReadsAppData,
        ToolSecurityImplications.ExportsData,
        ToolSecurityImplications.AccessesFileSystem,
        ToolSecurityImplications.UsesBinaryTransfer);
}
