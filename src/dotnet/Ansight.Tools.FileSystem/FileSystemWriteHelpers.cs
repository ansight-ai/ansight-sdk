namespace Ansight.Tools.FileSystem;

using System.Text;
using System.Text.Json.Nodes;

internal static class FileSystemWriteHelpers
{
    internal static string GetRequiredArgument(IReadOnlyDictionary<string, string> arguments, string key)
    {
        var value = FileSystemSandbox.GetString(arguments, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The argument '{key}' is required.");
        }

        return value;
    }

    internal static FileSystemSandbox.ResolvedPath ResolveFile(
        IReadOnlyDictionary<string, string> arguments,
        IReadOnlyDictionary<string, string> roots,
        string pathKey,
        string rootKey,
        bool requireExisting)
    {
        var pathArguments = CreatePathArguments(arguments, pathKey, rootKey);
        return FileSystemSandbox.ResolvePath(
            pathArguments,
            roots,
            requireExisting,
            expectDirectory: false);
    }

    internal static FileSystemSandbox.ResolvedPath ResolveDestinationFile(
        IReadOnlyDictionary<string, string> arguments,
        IReadOnlyDictionary<string, string> roots,
        FileSystemSandbox.ResolvedPath? sourceFile = null)
    {
        var destinationPath = GetRequiredArgument(arguments, "destinationPath");
        var destinationArguments = new Dictionary<string, string>
        {
            ["path"] = destinationPath
        };

        var destinationRoot = FileSystemSandbox.GetString(arguments, "destinationRoot");
        if (!string.IsNullOrWhiteSpace(destinationRoot))
        {
            destinationArguments["root"] = destinationRoot;
        }
        else if (sourceFile is not null && !Path.IsPathRooted(destinationPath))
        {
            destinationArguments["root"] = sourceFile.RootAlias;
        }

        return FileSystemSandbox.ResolvePath(
            destinationArguments,
            roots,
            requireExisting: false,
            expectDirectory: false);
    }

    internal static FileSystemSandbox.ResolvedPath ResolveDirectory(
        IReadOnlyDictionary<string, string> arguments,
        IReadOnlyDictionary<string, string> roots,
        string pathKey,
        string rootKey,
        bool requireExisting)
    {
        var pathArguments = CreatePathArguments(arguments, pathKey, rootKey);
        return FileSystemSandbox.ResolvePath(
            pathArguments,
            roots,
            requireExisting,
            expectDirectory: true);
    }

    internal static string GetSafeFileName(IReadOnlyDictionary<string, string> arguments)
    {
        var fileName = GetRequiredArgument(arguments, "fileName");
        if (Path.IsPathRooted(fileName) ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName is "." or ".." ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The argument 'fileName' must be a file name, not a path.");
        }

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("The argument 'fileName' contains invalid file name characters.");
        }

        return fileName;
    }

    internal static byte[] GetPushContent(IReadOnlyDictionary<string, string> arguments)
    {
        var hasBase64 = arguments.TryGetValue("contentBase64", out var contentBase64);
        var hasText = arguments.TryGetValue("text", out var text);

        if (hasBase64 == hasText)
        {
            throw new InvalidOperationException("Provide exactly one of 'contentBase64' or 'text'.");
        }

        if (hasBase64)
        {
            try
            {
                return Convert.FromBase64String(contentBase64 ?? string.Empty);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException("The argument 'contentBase64' must be valid base64.", exception);
            }
        }

        return Encoding.UTF8.GetBytes(text ?? string.Empty);
    }

    internal static bool EnsureDestinationFileCanBeWritten(
        FileSystemSandbox.ResolvedPath destinationFile,
        bool overwrite)
    {
        if (Directory.Exists(destinationFile.FullPath))
        {
            throw new InvalidOperationException($"The destination '{destinationFile.FullPath}' is an existing directory.");
        }

        var overwritten = File.Exists(destinationFile.FullPath);
        if (overwritten && !overwrite)
        {
            throw new IOException($"The destination file '{destinationFile.FullPath}' already exists. Set 'overwrite' to true to replace it.");
        }

        return overwritten;
    }

    internal static bool EnsureParentDirectoryExists(
        FileSystemSandbox.ResolvedPath destinationFile,
        bool createDirectory)
    {
        var parentDirectory = Path.GetDirectoryName(destinationFile.FullPath)
            ?? throw new InvalidOperationException($"The destination file '{destinationFile.FullPath}' does not have a parent directory.");

        if (Directory.Exists(parentDirectory))
        {
            return false;
        }

        if (!createDirectory)
        {
            throw new DirectoryNotFoundException($"The destination directory '{parentDirectory}' does not exist.");
        }

        Directory.CreateDirectory(parentDirectory);
        return true;
    }

    internal static void EnsureDifferentPaths(
        FileSystemSandbox.ResolvedPath sourceFile,
        FileSystemSandbox.ResolvedPath destinationFile)
    {
        if (string.Equals(sourceFile.FullPath, destinationFile.FullPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The source and destination file paths must be different.");
        }
    }

    internal static JsonObject CreateDestinationFilePayload(
        string operation,
        FileSystemSandbox.ResolvedPath destinationFile,
        IReadOnlyDictionary<string, string> roots,
        bool overwritten,
        bool createdDirectory)
    {
        var fileInfo = new FileInfo(destinationFile.FullPath);
        var payload = FileSystemContentDescriptor.CreateResolvedFilePayload(destinationFile, roots, fileInfo);
        payload["operation"] = operation;
        payload["overwritten"] = overwritten;
        payload["createdDirectory"] = createdDirectory;
        payload["capturedAtUtc"] = DateTime.UtcNow.ToString("O");
        return payload;
    }

    internal static JsonObject CreateFileTransferPayload(
        string operation,
        FileSystemSandbox.ResolvedPath sourceFile,
        FileSystemSandbox.ResolvedPath destinationFile,
        IReadOnlyDictionary<string, string> roots,
        bool overwritten,
        bool createdDirectory)
    {
        var payload = CreateDestinationFilePayload(operation, destinationFile, roots, overwritten, createdDirectory);
        payload["sourceRootAlias"] = sourceFile.RootAlias;
        payload["sourceRootPath"] = sourceFile.RootPath;
        payload["sourceFilePath"] = sourceFile.FullPath;
        payload["sourceRelativePath"] = Path.GetRelativePath(sourceFile.RootPath, sourceFile.FullPath);
        payload["destinationRootAlias"] = destinationFile.RootAlias;
        payload["destinationRootPath"] = destinationFile.RootPath;
        payload["destinationFilePath"] = destinationFile.FullPath;
        payload["destinationRelativePath"] = Path.GetRelativePath(destinationFile.RootPath, destinationFile.FullPath);
        return payload;
    }

    private static Dictionary<string, string> CreatePathArguments(
        IReadOnlyDictionary<string, string> arguments,
        string pathKey,
        string rootKey)
    {
        var pathArguments = new Dictionary<string, string>
        {
            ["path"] = GetRequiredArgument(arguments, pathKey)
        };

        var root = FileSystemSandbox.GetString(arguments, rootKey);
        if (!string.IsNullOrWhiteSpace(root))
        {
            pathArguments["root"] = root;
        }

        return pathArguments;
    }
}
