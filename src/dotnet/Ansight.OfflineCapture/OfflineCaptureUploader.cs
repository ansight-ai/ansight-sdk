namespace Ansight.OfflineCapture;

using Ansight.Network;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

/// <summary>
/// Uploads exported offline capture ZIP archives to the Ansight capture API.
/// </summary>
public sealed class OfflineCaptureUploader
{
    private static readonly HttpClient sharedHttpClient = CreateSharedHttpClient();
    private readonly HttpClient httpClient;

    /// <summary>
    /// Creates an uploader using a shared HTTP client.
    /// </summary>
    public OfflineCaptureUploader()
        : this(sharedHttpClient)
    {
    }

    /// <summary>
    /// Creates an uploader using a caller-provided HTTP client.
    /// </summary>
    public OfflineCaptureUploader(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Uploads an existing unencrypted offline capture ZIP archive.
    /// </summary>
    public async Task<OfflineCaptureUploadResult> UploadArchiveAsync(
        string archivePath,
        OfflineCaptureUploadMetadata metadata,
        OfflineCaptureUploadOptions options,
        IProgress<OfflineCaptureUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);

        var normalizedOptions = options.Normalize();
        var fullArchivePath = Path.GetFullPath(archivePath);
        var archiveInfo = new FileInfo(fullArchivePath);
        if (!archiveInfo.Exists)
        {
            throw new FileNotFoundException("Offline capture archive was not found.", fullArchivePath);
        }
        if (archiveInfo.Length <= 0)
        {
            throw new InvalidDataException("Offline capture archive is empty.");
        }
        if (string.IsNullOrWhiteSpace(metadata.SessionId))
        {
            throw new ArgumentException("Capture session ID is required.", nameof(metadata));
        }
        if (string.IsNullOrWhiteSpace(metadata.AppId))
        {
            throw new ArgumentException(
                "Capture app ID (package ID) is required and must match the API key scope.",
                nameof(metadata));
        }

        progress?.Report(new OfflineCaptureUploadProgress(
            OfflineCaptureUploadStage.Hashing,
            0,
            archiveInfo.Length,
            1));
        var archiveSha256 = await ComputeSha256Async(fullArchivePath, cancellationToken);
        progress?.Report(new OfflineCaptureUploadProgress(
            OfflineCaptureUploadStage.Hashing,
            archiveInfo.Length,
            archiveInfo.Length,
            1));

        var idempotencyKey = Guid.NewGuid().ToString("N");
        var createRequest = new CreateUploadRequest(
            "create",
            idempotencyKey,
            normalizedOptions.Title,
            new ArchiveRequest(archiveInfo.Length, archiveSha256),
            new CaptureRequest(
                metadata.SessionId.Trim(),
                metadata.AppId.Trim(),
                metadata.StartedAtUtc,
                metadata.StoppedAtUtc,
                NormalizeOptional(metadata.SdkVersion)));

        progress?.Report(new OfflineCaptureUploadProgress(
            OfflineCaptureUploadStage.CreatingUpload,
            0,
            archiveInfo.Length,
            1));
        var createResponse = await SendJsonWithRetryAsync<CreateUploadResponse>(
            normalizedOptions,
            createRequest,
            idempotencyKey,
            cancellationToken);

        var uploadId = RequireValue(createResponse.Upload?.Id, "The capture API did not return an upload ID.");
        if (!string.IsNullOrWhiteSpace(createResponse.SessionId))
        {
            return CreateResult(
                createResponse.SessionId,
                createResponse.SessionUrl,
                uploadId,
                archiveInfo.Length,
                archiveSha256,
                progress,
                1);
        }

        var uploadUrl = RequireAbsoluteUri(
            createResponse.UploadUrl,
            "The capture API did not return a signed upload URL.");
        await UploadArchiveWithRetryAsync(
            fullArchivePath,
            uploadUrl,
            archiveInfo.Length,
            normalizedOptions,
            progress,
            cancellationToken);

        progress?.Report(new OfflineCaptureUploadProgress(
            OfflineCaptureUploadStage.Finalizing,
            archiveInfo.Length,
            archiveInfo.Length,
            1));
        var completeResponse = await SendJsonWithRetryAsync<CompleteUploadResponse>(
            normalizedOptions,
            new CompleteUploadRequest("complete", uploadId),
            idempotencyKey: null,
            cancellationToken);
        var sessionId = RequireValue(
            completeResponse.SessionId,
            "The capture API completed the upload without returning a session ID.");

        return CreateResult(
            sessionId,
            completeResponse.SessionUrl,
            uploadId,
            archiveInfo.Length,
            archiveSha256,
            progress,
            1);
    }

    private async Task UploadArchiveWithRetryAsync(
        string archivePath,
        Uri uploadUrl,
        long archiveByteSize,
        OfflineCaptureUploadOptions options,
        IProgress<OfflineCaptureUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            try
            {
                progress?.Report(new OfflineCaptureUploadProgress(
                    OfflineCaptureUploadStage.Uploading,
                    0,
                    archiveByteSize,
                    attempt));

                await using var archiveStream = new FileStream(
                    archivePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var content = new ProgressStreamContent(
                    archiveStream,
                    archiveByteSize,
                    (bytesTransferred) => progress?.Report(new OfflineCaptureUploadProgress(
                        OfflineCaptureUploadStage.Uploading,
                        bytesTransferred,
                        archiveByteSize,
                        attempt)));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                content.Headers.ContentLength = archiveByteSize;

                using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
                {
                    Content = content
                };
                AnsightHttpMessageHandler.MarkAsInternalTraffic(request);
                request.Headers.TryAddWithoutValidation("x-upsert", "false");
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
                {
                    progress?.Report(new OfflineCaptureUploadProgress(
                        OfflineCaptureUploadStage.Uploading,
                        archiveByteSize,
                        archiveByteSize,
                        attempt));
                    return;
                }

                var failure = await CreateUploadExceptionAsync(response, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == options.MaximumAttempts)
                {
                    throw failure;
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException
                && attempt < options.MaximumAttempts)
            {
                // A fresh file stream is opened for every attempt.
            }

            await DelayForRetryAsync(options, attempt, cancellationToken);
        }
    }

    private async Task<TResponse> SendJsonWithRetryAsync<TResponse>(
        OfflineCaptureUploadOptions options,
        object body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
                {
                    Content = JsonContent.Create(body)
                };
                AnsightHttpMessageHandler.MarkAsInternalTraffic(request);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                request.Headers.TryAddWithoutValidation("x-client-info", "ansight-offline-capture-dotnet");
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    request.Headers.TryAddWithoutValidation("x-idempotency-key", idempotencyKey);
                }

                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<TResponse>(
                        cancellationToken: cancellationToken);
                    return result ?? throw new OfflineCaptureUploadException(
                        "The capture API returned an empty response.",
                        (int)response.StatusCode);
                }

                var failure = await CreateUploadExceptionAsync(response, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == options.MaximumAttempts)
                {
                    throw failure;
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException
                && attempt < options.MaximumAttempts)
            {
                // Retried below with the same idempotency key.
            }

            await DelayForRetryAsync(options, attempt, cancellationToken);
        }

        throw new InvalidOperationException("The capture upload retry loop exited unexpectedly.");
    }

    private static async Task<string> ComputeSha256Async(
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task<OfflineCaptureUploadException> CreateUploadExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? message = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
                cancellationToken: cancellationToken);
            message = NormalizeOptional(error?.Error);
        }
        catch (JsonException)
        {
            // Fall back to the response reason phrase.
        }

        message ??= response.ReasonPhrase ?? "Capture upload request failed.";
        return new OfflineCaptureUploadException(message, (int)response.StatusCode);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private static Task DelayForRetryAsync(
        OfflineCaptureUploadOptions options,
        int attempt,
        CancellationToken cancellationToken)
    {
        if (options.RetryDelay == TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        var delay = TimeSpan.FromMilliseconds(Math.Min(
            TimeSpan.FromMinutes(1).TotalMilliseconds,
            options.RetryDelay.TotalMilliseconds * multiplier));
        return Task.Delay(delay, cancellationToken);
    }

    private static OfflineCaptureUploadResult CreateResult(
        string sessionId,
        string? sessionUrl,
        string uploadId,
        long archiveByteSize,
        string archiveSha256,
        IProgress<OfflineCaptureUploadProgress>? progress,
        int attempt)
    {
        progress?.Report(new OfflineCaptureUploadProgress(
            OfflineCaptureUploadStage.Completed,
            archiveByteSize,
            archiveByteSize,
            attempt));
        return new OfflineCaptureUploadResult(
            uploadId,
            sessionId,
            Uri.TryCreate(sessionUrl, UriKind.Absolute, out var parsedSessionUrl) ? parsedSessionUrl : null,
            archiveByteSize,
            archiveSha256);
    }

    private static string RequireValue(string? value, string message)
    {
        return NormalizeOptional(value) ?? throw new OfflineCaptureUploadException(message);
    }

    private static Uri RequireAbsoluteUri(string? value, string message)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new OfflineCaptureUploadException(message);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static HttpClient CreateSharedHttpClient()
    {
        return new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private sealed record CreateUploadRequest(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("archive")] ArchiveRequest Archive,
        [property: JsonPropertyName("capture")] CaptureRequest Capture);

    private sealed record ArchiveRequest(
        [property: JsonPropertyName("byteSize")] long ByteSize,
        [property: JsonPropertyName("sha256")] string Sha256);

    private sealed record CaptureRequest(
        [property: JsonPropertyName("sessionId")] string SessionId,
        [property: JsonPropertyName("appId")] string AppId,
        [property: JsonPropertyName("startedAtUtc")] DateTimeOffset? StartedAtUtc,
        [property: JsonPropertyName("stoppedAtUtc")] DateTimeOffset? StoppedAtUtc,
        [property: JsonPropertyName("sdkVersion")] string? SdkVersion);

    private sealed record CompleteUploadRequest(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("uploadId")] string UploadId);

    private sealed record CreateUploadResponse(
        [property: JsonPropertyName("upload")] UploadResponse? Upload,
        [property: JsonPropertyName("uploadUrl")] string? UploadUrl,
        [property: JsonPropertyName("sessionId")] string? SessionId,
        [property: JsonPropertyName("sessionUrl")] string? SessionUrl);

    private sealed record CompleteUploadResponse(
        [property: JsonPropertyName("sessionId")] string? SessionId,
        [property: JsonPropertyName("sessionUrl")] string? SessionUrl);

    private sealed record UploadResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("status")] string? Status);

    private sealed record ErrorResponse(
        [property: JsonPropertyName("error")] string? Error);

    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream source;
        private readonly long length;
        private readonly Action<long> reportProgress;

        public ProgressStreamContent(Stream source, long length, Action<long> reportProgress)
        {
            this.source = source;
            this.length = length;
            this.reportProgress = reportProgress;
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[128 * 1024];
            long totalBytes = 0;
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytes += bytesRead;
                reportProgress(totalBytes);
            }
        }

        protected override bool TryComputeLength(out long computedLength)
        {
            computedLength = length;
            return true;
        }
    }
}
