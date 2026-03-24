using Ansight.Tools.SecureStorage;
using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

public sealed class SecureStorageToolsTests
{
    [Fact]
    public void WithSecureStorageTools_RegistersExpectedTools()
    {
        var options = Options.CreateBuilder()
            .WithSecureStorageTools(secure => secure.AllowKey("session_token"))
            .Build();

        Assert.Equal(
            ["secure.get_value", "secure.set_value", "secure.remove_key"],
            options.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public async Task GetSecureStorageValueTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new GetSecureStorageValueTool(SecureStorageToolsOptions.CreateBuilder()
            .AllowKey("session_token")
            .Build())
            .Execute(new Dictionary<string, string>
            {
                ["key"] = "session_token"
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("secure_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task SecureStorageTools_Execute_DenyAllByDefault()
    {
        var backend = new InMemorySecureStorageBackend("Redpoint");
        backend.SetSeed("session_token", "abc123");

        using var overrideScope = new SecureStorageBackendOverrideScope(_ => backend);
        var result = await new GetSecureStorageValueTool().Execute(new Dictionary<string, string>
        {
            ["key"] = "session_token"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("secure_get_failed", result.ErrorCode);
        Assert.Contains("not allowed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecureStorageTools_Execute_ReadWriteAndRemoveValues()
    {
        var backend = new InMemorySecureStorageBackend("Redpoint");
        using var overrideScope = new SecureStorageBackendOverrideScope(_ => backend);

        var options = SecureStorageToolsOptions.CreateBuilder()
            .WithStorageIdentifier("Redpoint")
            .AllowKey("session_token")
            .AllowKeyPrefix("com.redpoint.licensing.pass.")
            .Build();

        var setTool = new SetSecureStorageValueTool(options);
        var setResult = await setTool.Execute(new Dictionary<string, string>
        {
            ["key"] = "session_token",
            ["value"] = "abc123"
        });

        Assert.True(setResult.IsSuccess);
        Assert.Equal("Redpoint", Assert.IsType<JsonObject>(setResult.Payload)["store"]?.GetValue<string>());

        var getTool = new GetSecureStorageValueTool(options);
        var getResult = await getTool.Execute(new Dictionary<string, string>
        {
            ["key"] = "session_token"
        });

        Assert.True(getResult.IsSuccess);
        var getPayload = Assert.IsType<JsonObject>(getResult.Payload);
        Assert.True(getPayload["exists"]!.GetValue<bool>());
        Assert.Equal("abc123", getPayload["value"]?.GetValue<string>());

        var prefixedSetResult = await setTool.Execute(new Dictionary<string, string>
        {
            ["key"] = "com.redpoint.licensing.pass.premium",
            ["value"] = "encoded-pass"
        });

        Assert.True(prefixedSetResult.IsSuccess);

        var removeTool = new RemoveSecureStorageKeyTool(options);
        var removeResult = await removeTool.Execute(new Dictionary<string, string>
        {
            ["key"] = "session_token"
        });

        Assert.True(removeResult.IsSuccess);
        Assert.True(Assert.IsType<JsonObject>(removeResult.Payload)["removed"]!.GetValue<bool>());
    }

    private sealed class SecureStorageBackendOverrideScope : IDisposable
    {
        private readonly Func<SecureStorageToolsOptions, ISecureStorageBackend>? previousFactory;

        public SecureStorageBackendOverrideScope(Func<SecureStorageToolsOptions, ISecureStorageBackend> factory)
        {
            previousFactory = SecureStorageSupport.BackendFactoryOverride;
            SecureStorageSupport.BackendFactoryOverride = factory;
        }

        public void Dispose()
        {
            SecureStorageSupport.BackendFactoryOverride = previousFactory;
        }
    }

    private sealed class InMemorySecureStorageBackend : ISecureStorageBackend
    {
        private readonly string store;
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public InMemorySecureStorageBackend(string store)
        {
            this.store = store;
        }

        public SecureStorageValueResult GetValue(string key)
        {
            var exists = values.TryGetValue(key, out var value);
            return new SecureStorageValueResult(store, key, exists, value);
        }

        public SecureStorageWriteResult SetValue(string key, string value)
        {
            values[key] = value;
            return new SecureStorageWriteResult(store, key, true);
        }

        public SecureStorageRemoveResult RemoveKey(string key)
        {
            return new SecureStorageRemoveResult(store, key, values.Remove(key));
        }

        public void SetSeed(string key, string value)
        {
            values[key] = value;
        }
    }
}
