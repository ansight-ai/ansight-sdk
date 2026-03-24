using Ansight.Tools.Preferences;
using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

public sealed class PreferencesToolsTests
{
    [Fact]
    public void WithPreferencesTools_RegistersExpectedTools()
    {
        var options = Options.CreateBuilder()
            .WithPreferencesTools()
            .Build();

        Assert.Equal(
            [PreferencesToolIds.ListKeys, PreferencesToolIds.GetValue, PreferencesToolIds.SetValue, PreferencesToolIds.RemoveKey],
            options.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public async Task ListPreferenceKeysTool_Execute_FiltersByRestrictionsAndPrefix()
    {
        var backend = new InMemoryPreferencesBackend();
        backend.SetSeed("profile", "ansight.locale", PreferenceValueKind.String, "en-AU");
        backend.SetSeed("profile", "ansight.theme", PreferenceValueKind.String, "light");
        backend.SetSeed("profile", "secret.token", PreferenceValueKind.String, "hidden");

        using var overrideScope = new PreferencesBackendOverrideScope(() => backend);
        var tool = new ListPreferenceKeysTool(PreferencesToolsOptions.CreateBuilder()
            .AllowStore("profile")
            .AllowKeyPrefix("ansight.")
            .Build());

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["store"] = "profile",
            ["prefix"] = "ansight.",
            ["maxResults"] = "1"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal("profile", payload["store"]?.GetValue<string>());
        Assert.True(payload["truncated"]!.GetValue<bool>());

        var keys = Assert.IsType<JsonArray>(payload["keys"]).Select(node => node!.GetValue<string>()).ToArray();
        Assert.Equal(["ansight.locale"], keys);
    }

    [Fact]
    public async Task GetPreferenceValueTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new GetPreferenceValueTool().Execute(new Dictionary<string, string>
        {
            ["key"] = "ansight.test"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("prefs_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task PreferenceTools_Execute_ReadWriteAndRemoveValues()
    {
        var backend = new InMemoryPreferencesBackend();
        using var overrideScope = new PreferencesBackendOverrideScope(() => backend);
        var options = PreferencesToolsOptions.CreateBuilder()
            .AllowStore("profile")
            .AllowKey("ansight.enabled")
            .Build();

        var setTool = new SetPreferenceValueTool(options);
        var setResult = await setTool.Execute(new Dictionary<string, string>
        {
            ["store"] = "profile",
            ["key"] = "ansight.enabled",
            ["value"] = "true",
            ["valueType"] = "boolean"
        });

        Assert.True(setResult.IsSuccess);

        var getTool = new GetPreferenceValueTool(options);
        var getResult = await getTool.Execute(new Dictionary<string, string>
        {
            ["store"] = "profile",
            ["key"] = "ansight.enabled"
        });

        Assert.True(getResult.IsSuccess);
        var getPayload = Assert.IsType<JsonObject>(getResult.Payload);
        Assert.True(getPayload["exists"]!.GetValue<bool>());
        Assert.Equal("true", getPayload["value"]?.GetValue<string>());
        Assert.Equal("boolean", getPayload["valueType"]?.GetValue<string>());

        var removeTool = new RemovePreferenceKeyTool(options);
        var removeResult = await removeTool.Execute(new Dictionary<string, string>
        {
            ["store"] = "profile",
            ["key"] = "ansight.enabled"
        });

        Assert.True(removeResult.IsSuccess);
        Assert.True(Assert.IsType<JsonObject>(removeResult.Payload)["removed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task GetPreferenceValueTool_Execute_RejectsDisallowedKeys()
    {
        var backend = new InMemoryPreferencesBackend();
        backend.SetSeed("profile", "secret.token", PreferenceValueKind.String, "hidden");

        using var overrideScope = new PreferencesBackendOverrideScope(() => backend);
        var tool = new GetPreferenceValueTool(PreferencesToolsOptions.CreateBuilder()
            .AllowStore("profile")
            .AllowKeyPrefix("ansight.")
            .Build());

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["store"] = "profile",
            ["key"] = "secret.token"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("prefs_get_failed", result.ErrorCode);
        Assert.Contains("not allowed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PreferencesBackendOverrideScope : IDisposable
    {
        private readonly Func<IPreferencesBackend>? previousFactory;

        public PreferencesBackendOverrideScope(Func<IPreferencesBackend> factory)
        {
            previousFactory = PreferencesSupport.BackendFactoryOverride;
            PreferencesSupport.BackendFactoryOverride = factory;
        }

        public void Dispose()
        {
            PreferencesSupport.BackendFactoryOverride = previousFactory;
        }
    }

    private sealed class InMemoryPreferencesBackend : IPreferencesBackend
    {
        private readonly Dictionary<string, Dictionary<string, (PreferenceValueKind Kind, string? Value)>> stores = new(StringComparer.OrdinalIgnoreCase);

        public PreferenceListKeysResult ListKeys(string? store)
        {
            var resolvedStore = ResolveStore(store);
            var values = GetStore(resolvedStore);
            return new PreferenceListKeysResult(resolvedStore, values.Keys.ToList());
        }

        public PreferenceValueResult GetValue(string? store, string key)
        {
            var resolvedStore = ResolveStore(store);
            var values = GetStore(resolvedStore);
            if (!values.TryGetValue(key, out var entry))
            {
                return new PreferenceValueResult(resolvedStore, key, false, null, null);
            }

            return new PreferenceValueResult(resolvedStore, key, true, entry.Value, entry.Kind);
        }

        public PreferenceWriteResult SetValue(string? store, string key, PreferenceValueKind valueKind, string value)
        {
            var resolvedStore = ResolveStore(store);
            var values = GetStore(resolvedStore);
            values[key] = (valueKind, value);
            return new PreferenceWriteResult(resolvedStore, key, valueKind, true);
        }

        public PreferenceRemoveResult RemoveKey(string? store, string key)
        {
            var resolvedStore = ResolveStore(store);
            var values = GetStore(resolvedStore);
            return new PreferenceRemoveResult(resolvedStore, key, values.Remove(key));
        }

        public void SetSeed(string store, string key, PreferenceValueKind valueKind, string? value)
        {
            GetStore(store)[key] = (valueKind, value);
        }

        private Dictionary<string, (PreferenceValueKind Kind, string? Value)> GetStore(string store)
        {
            if (!stores.TryGetValue(store, out var values))
            {
                values = new Dictionary<string, (PreferenceValueKind Kind, string? Value)>(StringComparer.Ordinal);
                stores[store] = values;
            }

            return values;
        }

        private static string ResolveStore(string? store)
            => string.IsNullOrWhiteSpace(store) ? "default" : store.Trim();
    }
}
