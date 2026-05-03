namespace Ansight;

/// <summary>
/// Resolves a bundled pairing asset by its logical asset name.
/// </summary>
/// <param name="assetName">Logical asset name to load, such as <c>ansight.developer-pairing.json</c> or <c>ansight.json</c>.</param>
/// <param name="cancellationToken">Cancellation token for the load operation.</param>
/// <returns>The bundled asset contents, or <see langword="null"/> when the asset is unavailable.</returns>
public delegate Task<string?> HostConnectionBundledAssetLoader(
    string assetName,
    CancellationToken cancellationToken = default);
