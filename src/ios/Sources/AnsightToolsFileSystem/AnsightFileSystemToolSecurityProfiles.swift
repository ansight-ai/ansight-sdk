import AnsightCore
import Foundation

public enum AnsightFileSystemToolSecurityProfiles {
    public static let listDirectory = AnsightToolSecurity(
        level: .moderate,
        summary: "Reveals file and directory names inside configured sandbox roots.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.accessesFileSystem,
        ]
    )

    public static let readFile = AnsightToolSecurity(
        level: .high,
        summary: "Reads and exports file contents from configured sandbox roots.",
        implications: [
            AnsightToolSecurityImplications.readsAppData,
            AnsightToolSecurityImplications.exportsData,
            AnsightToolSecurityImplications.accessesFileSystem,
        ]
    )

    public static let getFileChecksum = AnsightToolSecurity(
        level: .moderate,
        summary: "Reads sandboxed file contents and returns content fingerprints.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.readsAppData,
            AnsightToolSecurityImplications.accessesFileSystem,
        ]
    )

    public static let downloadFile = AnsightToolSecurity(
        level: .high,
        summary: "Streams file contents out of the app sandbox in resumable chunks.",
        implications: [
            AnsightToolSecurityImplications.readsAppData,
            AnsightToolSecurityImplications.exportsData,
            AnsightToolSecurityImplications.accessesFileSystem,
        ]
    )

    public static let beginBinaryDownload = AnsightToolSecurity(
        level: .high,
        summary: "Transfers sandboxed file contents over the pairing channel as binary frames.",
        implications: [
            AnsightToolSecurityImplications.readsAppData,
            AnsightToolSecurityImplications.exportsData,
            AnsightToolSecurityImplications.accessesFileSystem,
            AnsightToolSecurityImplications.usesBinaryTransfer,
        ]
    )

    public static let pushFile = AnsightToolSecurity(
        level: .high,
        summary: "Writes caller-provided content into configured sandbox roots.",
        implications: [
            AnsightToolSecurityImplications.writesAppData,
            AnsightToolSecurityImplications.accessesFileSystem,
        ]
    )

    public static let copyFile = AnsightToolSecurity(
        level: .high,
        summary: "Copies sandboxed files and can create or replace app-owned data.",
        implications: [
            AnsightToolSecurityImplications.readsAppData,
            AnsightToolSecurityImplications.writesAppData,
            AnsightToolSecurityImplications.accessesFileSystem,
        ]
    )

    public static let moveFile = AnsightToolSecurity(
        level: .high,
        summary: "Moves sandboxed files and can rename, replace, or remove app-owned file paths.",
        implications: [
            AnsightToolSecurityImplications.writesAppData,
            AnsightToolSecurityImplications.deletesAppData,
            AnsightToolSecurityImplications.accessesFileSystem,
        ]
    )

    public static let deleteFile = AnsightToolSecurity(
        level: .critical,
        summary: "Deletes files from configured sandbox roots and can remove app data.",
        implications: [
            AnsightToolSecurityImplications.deletesAppData,
            AnsightToolSecurityImplications.accessesFileSystem,
        ]
    )
}
