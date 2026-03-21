namespace Ansight.Tools.FileSystem;

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

internal static class FileSystemContentDescriptor
{
    private static readonly UTF8Encoding strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly IReadOnlyDictionary<string, string> mimeTypesByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".aac"] = "audio/aac",
        [".bmp"] = "image/bmp",
        [".csv"] = "text/csv",
        [".dae"] = "model/vnd.collada+xml",
        [".db"] = "application/x-sqlite3",
        [".gif"] = "image/gif",
        [".glb"] = "model/gltf-binary",
        [".gltf"] = "model/gltf+json",
        [".gz"] = "application/gzip",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        [".htm"] = "text/html",
        [".html"] = "text/html",
        [".ini"] = "text/plain",
        [".jpeg"] = "image/jpeg",
        [".jpg"] = "image/jpeg",
        [".json"] = "application/json",
        [".jsonl"] = "application/x-ndjson",
        [".log"] = "text/plain",
        [".m4a"] = "audio/mp4",
        [".md"] = "text/markdown",
        [".mov"] = "video/quicktime",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
        [".obj"] = "model/obj",
        [".pdf"] = "application/pdf",
        [".plist"] = "application/xml",
        [".png"] = "image/png",
        [".sql"] = "application/sql",
        [".sqlite"] = "application/x-sqlite3",
        [".sqlite3"] = "application/x-sqlite3",
        [".stl"] = "model/stl",
        [".svg"] = "image/svg+xml",
        [".tar"] = "application/x-tar",
        [".toml"] = "application/toml",
        [".tsv"] = "text/tab-separated-values",
        [".txt"] = "text/plain",
        [".usdz"] = "model/vnd.usdz+zip",
        [".wav"] = "audio/wav",
        [".webp"] = "image/webp",
        [".xml"] = "application/xml",
        [".yaml"] = "application/yaml",
        [".yml"] = "application/yaml",
        [".zip"] = "application/zip"
    };

    internal sealed record EncodedContent(string RequestedEncoding, string ContentType, string Encoding, string? Text, string? Base64);

    internal static JsonObject CreateResolvedFilePayload(
        FileSystemSandbox.ResolvedPath resolvedFile,
        IReadOnlyDictionary<string, string> roots,
        FileInfo fileInfo)
    {
        var extension = GetFileExtension(fileInfo.Name);

        return new JsonObject
        {
            ["rootAlias"] = resolvedFile.RootAlias,
            ["rootPath"] = resolvedFile.RootPath,
            ["filePath"] = resolvedFile.FullPath,
            ["relativePath"] = Path.GetRelativePath(resolvedFile.RootPath, resolvedFile.FullPath),
            ["availableRoots"] = FileSystemSandbox.DescribeRoots(roots),
            ["fileName"] = fileInfo.Name,
            ["fileExtension"] = extension,
            ["mimeType"] = GetMimeType(fileInfo.Name),
            ["sizeBytes"] = fileInfo.Length,
            ["lastModifiedUtc"] = fileInfo.LastWriteTimeUtc.ToString("O"),
            ["version"] = CreateVersion(fileInfo)
        };
    }

    internal static EncodedContent EncodeContent(byte[] buffer, string mimeType, string? requestedEncoding)
    {
        var normalizedEncoding = NormalizeRequestedEncoding(requestedEncoding);
        return normalizedEncoding switch
        {
            "utf8" => EncodeUtf8(buffer, normalizedEncoding),
            "base64" => EncodeBase64(buffer, normalizedEncoding),
            _ => LooksLikeText(buffer, mimeType)
                ? EncodeUtf8(buffer, normalizedEncoding)
                : EncodeBase64(buffer, normalizedEncoding)
        };
    }

    internal static string NormalizeRequestedEncoding(string? requestedEncoding)
    {
        var normalized = requestedEncoding?.Trim().ToLowerInvariant();
        return normalized switch
        {
            null or "" => "auto",
            "auto" => "auto",
            "utf8" or "utf-8" => "utf8",
            "base64" => "base64",
            _ => throw new InvalidOperationException("The argument 'encoding' must be one of: auto, utf8, base64.")
        };
    }

    internal static string? GetFileExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? null : extension;
    }

    internal static string GetMimeType(string path)
    {
        var extension = GetFileExtension(path);
        if (extension is not null && mimeTypesByExtension.TryGetValue(extension, out var mimeType))
        {
            return mimeType;
        }

        return "application/octet-stream";
    }

    internal static string CreateVersion(FileInfo fileInfo)
    {
        return string.Concat(
            fileInfo.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    internal static bool LooksLikeText(byte[] bytes, string mimeType)
    {
        if (!FileSystemSandbox.IsUtf8(bytes))
        {
            return false;
        }

        if (IsTextLikeMimeType(mimeType))
        {
            return true;
        }

        foreach (var value in bytes)
        {
            if (value == 0)
            {
                return false;
            }

            if (value < 0x20 && value is not (byte)'\t' and not (byte)'\n' and not (byte)'\r')
            {
                return false;
            }
        }

        return true;
    }

    private static EncodedContent EncodeUtf8(byte[] buffer, string requestedEncoding)
    {
        try
        {
            return new EncodedContent(
                RequestedEncoding: requestedEncoding,
                ContentType: "text",
                Encoding: "utf-8",
                Text: strictUtf8.GetString(buffer),
                Base64: null);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException(
                "The requested chunk is not valid UTF-8. Use 'base64' for binary-safe transfer.",
                exception);
        }
    }

    private static EncodedContent EncodeBase64(byte[] buffer, string requestedEncoding)
        => new(
            RequestedEncoding: requestedEncoding,
            ContentType: "binary",
            Encoding: "base64",
            Text: null,
            Base64: Convert.ToBase64String(buffer));

    private static bool IsTextLikeMimeType(string mimeType)
    {
        if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return mimeType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("application/yaml", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("application/x-ndjson", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("application/sql", StringComparison.OrdinalIgnoreCase) ||
            mimeType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
            mimeType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);
    }
}
