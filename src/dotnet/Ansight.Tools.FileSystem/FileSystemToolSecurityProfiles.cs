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

    public static ToolSecurity GetFileChecksum { get; } = new(
        ToolSecurityLevel.Moderate,
        "Reads sandboxed file contents and returns content fingerprints.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.ReadsAppData,
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

    public static ToolSecurity PushFile { get; } = new(
        ToolSecurityLevel.High,
        "Writes caller-provided content into configured sandbox roots.",
        ToolSecurityImplications.WritesAppData,
        ToolSecurityImplications.AccessesFileSystem);

    public static ToolSecurity CopyFile { get; } = new(
        ToolSecurityLevel.High,
        "Copies sandboxed files and can create or replace app-owned data.",
        ToolSecurityImplications.ReadsAppData,
        ToolSecurityImplications.WritesAppData,
        ToolSecurityImplications.AccessesFileSystem);

    public static ToolSecurity MoveFile { get; } = new(
        ToolSecurityLevel.High,
        "Moves sandboxed files and can rename, replace, or remove app-owned file paths.",
        ToolSecurityImplications.WritesAppData,
        ToolSecurityImplications.DeletesAppData,
        ToolSecurityImplications.AccessesFileSystem);

    public static ToolSecurity DeleteFile { get; } = new(
        ToolSecurityLevel.Critical,
        "Deletes files from configured sandbox roots and can remove app data.",
        ToolSecurityImplications.DeletesAppData,
        ToolSecurityImplications.AccessesFileSystem);
}
