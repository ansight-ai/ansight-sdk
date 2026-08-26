using Ansight.Network;

namespace Ansight.UnitTests;

public sealed class NetworkRequestSanitizerTests
{
    [Fact]
    public void Normalize_RedactsCredentialHeadersAndQueryValues()
    {
        var result = NetworkRequestSanitizer.Sanitize(new NetworkRequestRecord
        {
            Id = "request-001",
            Source = "test",
            StartedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
            CompletedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:01Z"),
            DurationMilliseconds = 1000,
            Method = "post",
            Url = "https://user:password@example.test/path?api_key=abc&safe=yes",
            RequestHeaders =
            [
                new NetworkHeader { Name = "Authorization", Value = "Bearer abc" },
                new NetworkHeader { Name = "X-Custom", Value = "visible" }
            ],
            ErrorMessage = "Request failed for https://example.test/path?token=error-secret and api_key=standalone-secret"
        });

        Assert.NotNull(result);
        Assert.Equal("POST", result.Method);
        Assert.DoesNotContain("password", result.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", result.Url, StringComparison.Ordinal);
        Assert.Contains("safe=yes", result.Url, StringComparison.Ordinal);
        Assert.Equal("<redacted>", result.RequestHeaders[0].Value);
        Assert.Equal("visible", result.RequestHeaders[1].Value);
        Assert.DoesNotContain("error-secret", result.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("standalone-secret", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("<redacted>", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_AppliesAppPolicyAndDefaultRedactionAgain()
    {
        var result = NetworkRequestSanitizer.Sanitize(
            new NetworkRequestRecord
            {
                Id = "request-002",
                Source = "test",
                StartedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
                CompletedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:01Z"),
                DurationMilliseconds = 1000,
                Method = "GET",
                Url = "https://example.test/path?tenant=acme&safe=yes",
                RequestHeaders =
                [
                    new NetworkHeader { Name = "X-Tenant", Value = "acme" },
                    new NetworkHeader { Name = "Accept", Value = "application/json" }
                ],
                RequestBodySizeBytes = 42
            },
            new NetworkRequestSanitizationOptions
            {
                AdditionalSensitiveHeaderNames = ["X-Tenant"],
                AdditionalSensitiveQueryParameterNames = ["tenant"],
                IncludeBodySizes = false,
                RequestSanitizer = request => request with
                {
                    RequestHeaders =
                    [
                        .. request.RequestHeaders,
                        new NetworkHeader { Name = "Authorization", Value = "reintroduced-secret" }
                    ]
                }
            });

        Assert.NotNull(result);
        Assert.Contains("tenant=%3Credacted%3E", result.Url, StringComparison.Ordinal);
        Assert.Null(result.RequestBodySizeBytes);
        Assert.Equal("<redacted>", result.RequestHeaders[0].Value);
        Assert.Equal("<redacted>", result.RequestHeaders[2].Value);
    }

    [Fact]
    public void Sanitize_CanExplicitlyRetainSensitiveValues()
    {
        var result = NetworkRequestSanitizer.Sanitize(
            new NetworkRequestRecord
            {
                Id = "request-raw",
                Source = "test",
                StartedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
                CompletedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:01Z"),
                DurationMilliseconds = 1000,
                Method = "POST",
                Url = "https://example.test/path?access_token=raw-token",
                RequestHeaders =
                [
                    new NetworkHeader { Name = "Authorization", Value = "Bearer raw-token" }
                ],
                RequestBody = new NetworkBody
                {
                    ContentType = "application/json",
                    Encoding = NetworkBody.Utf8Encoding,
                    Data = "{\"token\":\"raw-token\"}",
                    CapturedBytes = 21,
                    TotalBytes = 21,
                    Truncated = false
                }
            },
            new NetworkRequestSanitizationOptions
            {
                RedactSensitiveData = false
            });

        Assert.NotNull(result);
        Assert.Contains("access_token=raw-token", result.Url, StringComparison.Ordinal);
        Assert.Equal("Bearer raw-token", result.RequestHeaders[0].Value);
        Assert.Contains("raw-token", result.RequestBody!.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_ReturnsNullWhenAppSuppressesRequest()
    {
        var result = NetworkRequestSanitizer.Sanitize(
            new NetworkRequestRecord
            {
                Id = "request-003",
                Source = "test",
                StartedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
                CompletedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:01Z"),
                DurationMilliseconds = 1000,
                Method = "GET",
                Url = "https://health.example.test"
            },
            new NetworkRequestSanitizationOptions
            {
                RequestSanitizer = _ => null
            });

        Assert.Null(result);
    }

    [Fact]
    public void Sanitize_RedactsCloudSignedUrlsAndTextBodies()
    {
        var result = NetworkRequestSanitizer.Sanitize(new NetworkRequestRecord
        {
            Id = "request-cloud",
            Source = "test",
            StartedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
            CompletedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:01Z"),
            DurationMilliseconds = 1000,
            Method = "PUT",
            Url = "https://account.blob.core.windows.net/container/blob?sv=2025-01-05&sp=rw&se=2030-01-01&sig=azure-secret&safe=yes",
            RequestBody = new NetworkBody
            {
                ContentType = "application/json",
                Encoding = NetworkBody.Utf8Encoding,
                Data = "{\"token\":\"body-secret\",\"visible\":\"yes\"}",
                CapturedBytes = 46,
                TotalBytes = 46,
                Truncated = false
            }
        });

        Assert.NotNull(result);
        Assert.DoesNotContain("azure-secret", result.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("2025-01-05", result.Url, StringComparison.Ordinal);
        Assert.Contains("safe=yes", result.Url, StringComparison.Ordinal);
        Assert.NotNull(result.RequestBody);
        Assert.DoesNotContain("body-secret", result.RequestBody.Data, StringComparison.Ordinal);
        Assert.Contains("visible", result.RequestBody.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsBuilder_IndependentlyControlsBodiesAndHonorsLargeLimits()
    {
        var options = new NetworkRequestSanitizationOptionsBuilder()
            .WithoutRequestBodies()
            .WithResponseBodies()
            .WithMaximumBodyBytes(8 * 1024 * 1024)
            .Build();

        Assert.False(options.CaptureRequestBody);
        Assert.True(options.CaptureResponseBody);
        Assert.Equal(8 * 1024 * 1024, options.MaximumBodyBytes);
    }

    [Fact]
    public void SanitizeForTransport_PreservesBodyAlreadyBoundedByTheAppPolicy()
    {
        var data = new string('x', 128 * 1024);
        var result = NetworkRequestSanitizer.SanitizeForTransport(new NetworkRequestRecord
        {
            Id = "request-large",
            Source = "test",
            StartedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
            CompletedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:01Z"),
            DurationMilliseconds = 1000,
            Method = "POST",
            Url = "https://example.test/upload",
            RequestBody = new NetworkBody
            {
                ContentType = "text/plain",
                Encoding = NetworkBody.Utf8Encoding,
                Data = data,
                CapturedBytes = data.Length,
                TotalBytes = data.Length,
                Truncated = false
            }
        });

        Assert.NotNull(result.RequestBody);
        Assert.Equal(128 * 1024, result.RequestBody.CapturedBytes);
        Assert.Equal(data, result.RequestBody.Data);
    }
}
