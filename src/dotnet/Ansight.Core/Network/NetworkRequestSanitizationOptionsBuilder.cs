namespace Ansight.Network;

/// <summary>
/// Fluent body-capture controls for <see cref="NetworkRequestSanitizationOptions"/>.
/// Network capture itself remains opt-in by installing an <see cref="AnsightHttpMessageHandler"/>.
/// </summary>
public sealed class NetworkRequestSanitizationOptionsBuilder
{
    private readonly NetworkRequestSanitizationOptions initialOptions;
    private bool captureRequestBody;
    private bool captureResponseBody;
    private bool captureBinaryBodies;
    private int maximumBodyBytes;

    public NetworkRequestSanitizationOptionsBuilder(
        NetworkRequestSanitizationOptions? initialOptions = null)
    {
        this.initialOptions = initialOptions ?? new NetworkRequestSanitizationOptions();
        captureRequestBody = this.initialOptions.CaptureRequestBody;
        captureResponseBody = this.initialOptions.CaptureResponseBody;
        captureBinaryBodies = this.initialOptions.CaptureBinaryBodies;
        maximumBodyBytes = this.initialOptions.MaximumBodyBytes;
    }

    public NetworkRequestSanitizationOptionsBuilder WithRequestBodies(bool include = true)
    {
        captureRequestBody = include;
        return this;
    }

    public NetworkRequestSanitizationOptionsBuilder WithoutRequestBodies()
        => WithRequestBodies(false);

    public NetworkRequestSanitizationOptionsBuilder WithResponseBodies(bool include = true)
    {
        captureResponseBody = include;
        return this;
    }

    public NetworkRequestSanitizationOptionsBuilder WithoutResponseBodies()
        => WithResponseBodies(false);

    public NetworkRequestSanitizationOptionsBuilder WithMaximumBodyBytes(int value)
    {
        maximumBodyBytes = value;
        return this;
    }

    public NetworkRequestSanitizationOptionsBuilder WithBinaryBodies(bool include = true)
    {
        captureBinaryBodies = include;
        return this;
    }

    public NetworkRequestSanitizationOptions Build()
        => new()
        {
            IncludeRequestHeaders = initialOptions.IncludeRequestHeaders,
            IncludeResponseHeaders = initialOptions.IncludeResponseHeaders,
            IncludeQueryString = initialOptions.IncludeQueryString,
            IncludeBodySizes = initialOptions.IncludeBodySizes,
            CaptureRequestBody = captureRequestBody,
            CaptureResponseBody = captureResponseBody,
            MaximumBodyBytes = maximumBodyBytes,
            CaptureBinaryBodies = captureBinaryBodies,
            AdditionalSensitiveHeaderNames = initialOptions.AdditionalSensitiveHeaderNames,
            AdditionalSensitiveQueryParameterNames = initialOptions.AdditionalSensitiveQueryParameterNames,
            UrlSanitizer = initialOptions.UrlSanitizer,
            RequestSanitizer = initialOptions.RequestSanitizer
        };
}
