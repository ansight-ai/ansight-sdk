namespace Ansight.Artifacts;

using System.Text;

/// <summary>
/// Factory methods for common artifact payload sources.
/// </summary>
public static class ArtifactPayload
{
    /// <summary>
    /// Creates a payload from text encoded as UTF-8 unless another encoding is supplied.
    /// </summary>
    public static IArtifactPayload FromText(string text, Encoding? encoding = null)
        => new TextArtifactPayload(text, encoding ?? Encoding.UTF8);

    /// <summary>
    /// Creates a payload from an in-memory byte array.
    /// </summary>
    public static IArtifactPayload FromBytes(byte[] bytes)
        => new ByteArrayArtifactPayload(bytes);

    /// <summary>
    /// Creates a payload from a stream factory.
    /// </summary>
    public static IArtifactPayload FromStream(
        Func<CancellationToken, ValueTask<Stream>> openStream,
        long? sizeBytes = null)
        => new StreamArtifactPayload(openStream, sizeBytes);

    /// <summary>
    /// Creates a payload from an app-local file. The path is used only as an SDK read source.
    /// </summary>
    public static IArtifactPayload FromFile(string path)
        => new FileArtifactPayload(path);

    private sealed class TextArtifactPayload : IArtifactPayload
    {
        private readonly string text;
        private readonly Encoding encoding;

        internal TextArtifactPayload(string text, Encoding encoding)
        {
            this.text = text ?? throw new ArgumentNullException(nameof(text));
            this.encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
        }

        public long? SizeBytes => encoding.GetByteCount(text);

        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            var bytes = encoding.GetBytes(text);
            return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }
    }

    private sealed class ByteArrayArtifactPayload : IArtifactPayload
    {
        private readonly byte[] bytes;

        internal ByteArrayArtifactPayload(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            this.bytes = bytes.ToArray();
        }

        public long? SizeBytes => bytes.LongLength;

        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    private sealed class StreamArtifactPayload : IArtifactPayload
    {
        private readonly Func<CancellationToken, ValueTask<Stream>> openStream;

        internal StreamArtifactPayload(
            Func<CancellationToken, ValueTask<Stream>> openStream,
            long? sizeBytes)
        {
            this.openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
            SizeBytes = sizeBytes;
        }

        public long? SizeBytes { get; }

        public async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            var stream = await openStream(cancellationToken);
            if (stream is null)
            {
                throw new InvalidOperationException("Artifact stream factory returned null.");
            }

            if (!stream.CanRead)
            {
                await stream.DisposeAsync();
                throw new InvalidOperationException("Artifact stream factory returned a stream that cannot be read.");
            }

            return stream;
        }
    }

    private sealed class FileArtifactPayload : IArtifactPayload
    {
        private readonly string path;

        internal FileArtifactPayload(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            this.path = path;
        }

        public long? SizeBytes
        {
            get
            {
                var fileInfo = new FileInfo(path);
                return fileInfo.Exists ? fileInfo.Length : null;
            }
        }

        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            return ValueTask.FromResult<Stream>(stream);
        }
    }
}
