namespace Ansight.Artifacts;

using System.Text.Json.Nodes;
using Ansight.Tools;

/// <summary>
/// Remote tool that queries app-provided artifact definitions.
/// </summary>
public sealed class QueryArtifactsTool : ITool
{
    private readonly Func<ArtifactRegistry> providersFactory;

    /// <summary>
    /// Creates a query tool over the supplied artifact providers.
    /// </summary>
    public QueryArtifactsTool(ArtifactRegistry providers)
        : this(() => providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
    }

    internal QueryArtifactsTool(Func<ArtifactRegistry> providersFactory)
    {
        this.providersFactory = providersFactory ?? throw new ArgumentNullException(nameof(providersFactory));
    }

    public string Category => "artifacts";

    public ToolScope Scope => ToolScope.Read;

    public string Id => ArtifactToolIds.Query;

    public string Name => "Query Artifacts";

    public string Description => "Queries app-provided artifact providers and currently requestable artifact definitions.";

    public string Keywords => "artifact artifacts query catalog provider export snapshot";

    public ToolSchema ArgumentsSchema => ArtifactToolSchemas.QueryArguments;

    public ToolSchema ResultSchema => ArtifactToolSchemas.QueryResult;

    public ToolSecurity Security => ArtifactToolSecurityProfiles.Query;

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var requestId = ArtifactToolArgumentReader.GetString(arguments, ToolExecutionArgumentNames.RequestId) ?? Guid.NewGuid().ToString("N");
        var sessionId = ArtifactToolArgumentReader.GetString(arguments, ToolExecutionArgumentNames.SessionId);
        var capturedAtUtc = DateTimeOffset.UtcNow;
        var context = new ArtifactQueryContext(requestId, sessionId, capturedAtUtc);
        var providerFilter = ArtifactToolArgumentReader.GetString(arguments, "providerId");
        var categoryFilter = ArtifactToolArgumentReader.GetString(arguments, "category");
        var kindFilter = ArtifactToolArgumentReader.GetString(arguments, "kind");
        var tagFilter = ArtifactToolArgumentReader.GetString(arguments, "tag");

        var providerArray = new JsonArray();
        var artifactArray = new JsonArray();
        var providers = providersFactory() ?? ArtifactRegistry.Empty;

        foreach (var provider in providers)
        {
            if (!string.IsNullOrWhiteSpace(providerFilter) &&
                !string.Equals(provider.Descriptor.Id, providerFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var definitions = await provider.QueryAsync(context, CancellationToken.None);
                providerArray.Add(ArtifactToolJson.ToJson(provider.Descriptor));

                foreach (var definition in definitions ?? Array.Empty<ArtifactDefinition>())
                {
                    if (!Matches(definition, categoryFilter, kindFilter, tagFilter))
                    {
                        continue;
                    }

                    artifactArray.Add(ArtifactToolJson.ToJson(provider.Descriptor.Id, definition));
                }
            }
            catch (Exception exception)
            {
                providerArray.Add(ArtifactToolJson.ToJson(provider.Descriptor, exception.Message));
            }
        }

        var payload = new JsonObject
        {
            ["providers"] = providerArray,
            ["artifacts"] = artifactArray,
            ["providerCount"] = providerArray.Count,
            ["artifactCount"] = artifactArray.Count,
            ["capturedAtUtc"] = capturedAtUtc.ToString("O")
        };

        return ToolResult.Success(payload);
    }

    private static bool Matches(
        ArtifactDefinition definition,
        string? category,
        string? kind,
        string? tag)
    {
        if (!string.IsNullOrWhiteSpace(category) &&
            !string.Equals(definition.Category, category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(kind) &&
            !string.Equals(definition.Kind, kind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(tag) &&
            !definition.Tags.Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }
}
