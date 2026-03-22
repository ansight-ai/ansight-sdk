using System.Text;
using Microsoft.Maui.Storage;

namespace Ansight.Maui;

/// <summary>
/// MAUI-specific helpers for Ansight host pairing.
/// </summary>
public static class HostPairingMauiExtensions
{
    /// <summary>
    /// Configures bundled MAUI app-package pairing assets for runtime-owned host pairing.
    /// </summary>
    public static Options.OptionsBuilder WithMauiBundledPairingAssets(
        this Options.OptionsBuilder builder,
        string? bundledDeveloperAssetName = HostPairingOptions.BundledDeveloperAssetName,
        string? bundledAssetName = HostPairingOptions.BundledAssetName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigureHostPairing(options =>
        {
            ConfigureBundledAsset(
                bundledDeveloperAssetName,
                loader => options.BundledDeveloperProfileLoader = loader);
            ConfigureBundledAsset(
                bundledAssetName,
                loader => options.BundledProfileLoader = loader);
        });
    }

    /// <summary>
    /// Uses a registered MAUI payload reader to load a pairing payload and connect through the runtime-owned pairing flow.
    /// </summary>
    public static async Task<HostPairingActionResult> ConnectFromPayloadReaderAsync(
        this IHostPairing hostPairing,
        IHostPairingPayloadReader payloadReader,
        HostPairingPayloadReadRequest request,
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostPairing);
        ArgumentNullException.ThrowIfNull(payloadReader);
        ArgumentNullException.ThrowIfNull(request);

        if (!payloadReader.CanRead(request.Kind))
        {
            return HostPairingActionResult.FromFailure(
                $"No host pairing payload reader is registered for {request.Kind}.",
                HostPairingActionKind.ConnectFromPayload,
                HostPairingSource.PayloadReader);
        }

        string? payload;
        try
        {
            payload = await payloadReader.ReadPayloadAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HostPairingActionResult.FromFailure(
                $"Failed to read a pairing payload: {ex.Message}",
                HostPairingActionKind.ConnectFromPayload,
                HostPairingSource.PayloadReader);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return HostPairingActionResult.FromFailure(
                "No pairing payload was provided.",
                HostPairingActionKind.ConnectFromPayload,
                HostPairingSource.PayloadReader);
        }

        return await hostPairing.ConnectFromPayloadAsync(
            payload,
            request.SourceDescription,
            clientName,
            progress,
            cancellationToken);
    }

    private static void ConfigureBundledAsset(
        string? assetName,
        Action<Func<CancellationToken, Task<string?>>?> setLoader)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            setLoader(null);
            return;
        }

        setLoader(cancellationToken => TryLoadBundledTextAssetAsync(assetName.Trim(), cancellationToken));
    }

    private static async Task<string?> TryLoadBundledTextAssetAsync(string assetName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(assetName);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = await reader.ReadToEndAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

}
