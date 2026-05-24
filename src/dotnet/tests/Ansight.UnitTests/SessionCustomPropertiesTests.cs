using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

public sealed class SessionCustomPropertiesTests
{
    [Fact]
    public void Register_GroupsPropertiesAndOverwritesExistingValues()
    {
        var properties = new SessionCustomProperties()
            .Register(" app ", " tenant ", "acme")
            .Register("app", "tenant", "contoso")
            .Register("flags", "beta", true)
            .Register("limits", "maxUsers", 42);

        var json = properties.ToJsonObject();

        Assert.Equal("contoso", json["app"]?["tenant"]?.GetValue<string>());
        Assert.True(json["flags"]?["beta"]?.GetValue<bool>());
        Assert.Equal(42, json["limits"]?["maxUsers"]?.GetValue<int>());
    }

    [Fact]
    public void Remove_WhenLastGroupPropertyIsRemoved_RemovesGroup()
    {
        var properties = new SessionCustomProperties()
            .Register("app", "tenant", "acme");

        var removed = properties.Remove("app", "tenant");

        Assert.True(removed);
        Assert.True(properties.IsEmpty);
        Assert.Empty(properties.ToJsonObject());
    }

    [Fact]
    public void Register_WhenValueIsComplexJson_Throws()
    {
        var properties = new SessionCustomProperties();

        Assert.Throws<ArgumentException>(() => properties.Register("app", "object", new JsonObject()));
        Assert.Throws<ArgumentException>(() => properties.Register("app", "array", new JsonArray()));
    }
}
