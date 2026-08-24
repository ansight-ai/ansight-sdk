namespace Ansight.Tools.FileSystem;

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

public sealed class GetFileChecksumTool : ITool
{
    private const int BufferSize = 128 * 1024;
    private static readonly IReadOnlyList<string> defaultAlgorithms = new[] { "sha256" };
    private static readonly IReadOnlyList<string> allAlgorithms = new[] { "md5", "sha1", "sha256", "sha384", "sha512", "crc32" };
    private readonly FileSystemToolsOptions options;

    public GetFileChecksumTool(FileSystemToolsOptions? options = null)
    {
        this.options = options ?? FileSystemToolsOptions.Default;
    }

    public string Category => "files";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => FileSystemToolIds.GetFileChecksum;

    public string Name => "Get File Checksum";

    public string Description => "Computes checksum digests for a sandboxed file without returning the file contents.";

    public string Keywords => "filesystem file checksum hash digest md5 sha1 sha256 sha384 sha512 crc32 sandbox";

    public ToolSchema ArgumentsSchema => FileSystemToolSchemas.GetFileChecksumArguments;

    public ToolSchema ResultSchema => FileSystemToolSchemas.GetFileChecksumResult;

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var roots = FileSystemSandbox.GetRoots(options);
            var resolvedFile = FileSystemSandbox.ResolvePath(arguments, roots, requireExisting: true, expectDirectory: false);
            var requestedAlgorithms = GetRequestedAlgorithms(arguments);
            var fileInfo = new FileInfo(resolvedFile.FullPath);

            var checksums = await ComputeChecksumsAsync(resolvedFile.FullPath, requestedAlgorithms);
            var payload = FileSystemContentDescriptor.CreateResolvedFilePayload(resolvedFile, roots, fileInfo);
            payload["checksums"] = checksums;
            payload["capturedAtUtc"] = DateTime.UtcNow.ToString("O");

            return ToolResult.Success(payload);
        }
        catch (Exception exception)
        {
            return ToolResult.Failure(exception.Message, errorCode: "filesystem_checksum_failed");
        }
    }

    private static IReadOnlyList<string> GetRequestedAlgorithms(IReadOnlyDictionary<string, string> arguments)
    {
        var requested = FileSystemSandbox.GetString(arguments, "algorithms");
        if (string.IsNullOrWhiteSpace(requested))
        {
            return defaultAlgorithms;
        }

        var algorithms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in requested.Split(
            new[] { ',', ';', ' ', '\t', '\r', '\n' },
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var algorithm = NormalizeAlgorithm(token);
            if (algorithm == "all")
            {
                return allAlgorithms;
            }

            if (seen.Add(algorithm))
            {
                algorithms.Add(algorithm);
            }
        }

        if (algorithms.Count == 0)
        {
            throw new InvalidOperationException("At least one checksum algorithm must be requested.");
        }

        return algorithms;
    }

    private static string NormalizeAlgorithm(string algorithm)
    {
        var normalized = algorithm
            .Trim()
            .ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        return normalized switch
        {
            "all" => "all",
            "crc32" or "crc" => "crc32",
            "md5" => "md5",
            "sha1" => "sha1",
            "sha256" or "sha2256" => "sha256",
            "sha384" or "sha2384" => "sha384",
            "sha512" or "sha2512" => "sha512",
            _ => throw new InvalidOperationException(
                $"Unsupported checksum algorithm '{algorithm}'. Supported algorithms: {string.Join(", ", allAlgorithms)}.")
        };
    }

    private static async Task<JsonArray> ComputeChecksumsAsync(string filePath, IReadOnlyList<string> requestedAlgorithms)
    {
        var calculators = requestedAlgorithms.Select(CreateCalculator).ToList();
        try
        {
            var buffer = new byte[BufferSize];
            await using (var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: BufferSize,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    var bytes = buffer.AsSpan(0, bytesRead);
                    foreach (var calculator in calculators)
                    {
                        calculator.Append(bytes);
                    }
                }
            }

            var checksums = new JsonArray();
            foreach (var calculator in calculators)
            {
                checksums.Add(calculator.Complete());
            }

            return checksums;
        }
        finally
        {
            foreach (var calculator in calculators)
            {
                calculator.Dispose();
            }
        }
    }

    private static ChecksumCalculator CreateCalculator(string algorithm)
        => algorithm switch
        {
            "crc32" => ChecksumCalculator.CreateCrc32(),
            "md5" => ChecksumCalculator.CreateHash(algorithm, HashAlgorithmName.MD5),
            "sha1" => ChecksumCalculator.CreateHash(algorithm, HashAlgorithmName.SHA1),
            "sha256" => ChecksumCalculator.CreateHash(algorithm, HashAlgorithmName.SHA256),
            "sha384" => ChecksumCalculator.CreateHash(algorithm, HashAlgorithmName.SHA384),
            "sha512" => ChecksumCalculator.CreateHash(algorithm, HashAlgorithmName.SHA512),
            _ => throw new InvalidOperationException(
                $"Unsupported checksum algorithm '{algorithm}'. Supported algorithms: {string.Join(", ", allAlgorithms)}.")
        };

    private sealed class ChecksumCalculator : IDisposable
    {
        private readonly IncrementalHash? incrementalHash;
        private readonly Crc32Checksum? crc32Checksum;

        private ChecksumCalculator(string algorithm, IncrementalHash? incrementalHash, Crc32Checksum? crc32Checksum)
        {
            Algorithm = algorithm;
            this.incrementalHash = incrementalHash;
            this.crc32Checksum = crc32Checksum;
        }

        internal string Algorithm { get; }

        internal static ChecksumCalculator CreateHash(string algorithm, HashAlgorithmName algorithmName)
            => new(algorithm, IncrementalHash.CreateHash(algorithmName), null);

        internal static ChecksumCalculator CreateCrc32()
            => new("crc32", null, new Crc32Checksum());

        internal void Append(ReadOnlySpan<byte> bytes)
        {
            incrementalHash?.AppendData(bytes);
            crc32Checksum?.Append(bytes);
        }

        internal JsonObject Complete()
        {
            var checksum = incrementalHash is not null
                ? Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant()
                : crc32Checksum!.Complete();

            return new JsonObject
            {
                ["algorithm"] = Algorithm,
                ["checksum"] = checksum,
                ["encoding"] = "hex"
            };
        }

        public void Dispose()
        {
            incrementalHash?.Dispose();
        }
    }

    private sealed class Crc32Checksum
    {
        private static readonly uint[] table = CreateTable();
        private uint value = 0xFFFFFFFFu;

        internal void Append(ReadOnlySpan<byte> bytes)
        {
            foreach (var inputByte in bytes)
            {
                var tableIndex = (byte)(value ^ inputByte);
                value = table[tableIndex] ^ (value >> 8);
            }
        }

        internal string Complete()
            => (~value).ToString("x8", CultureInfo.InvariantCulture);

        private static uint[] CreateTable()
        {
            var result = new uint[256];
            for (var index = 0; index < result.Length; index++)
            {
                var current = (uint)index;
                for (var bit = 0; bit < 8; bit++)
                {
                    current = (current & 1u) == 1u
                        ? 0xEDB88320u ^ (current >> 1)
                        : current >> 1;
                }

                result[index] = current;
            }

            return result;
        }
    }
}
