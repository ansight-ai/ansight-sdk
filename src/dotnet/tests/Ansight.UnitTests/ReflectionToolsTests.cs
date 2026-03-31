using Ansight.Tools;
using Ansight.Tools.Reflection;
using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

public sealed class ReflectionToolsTests
{
    [Fact]
    public void WithReflectionTools_RegistersExpectedTools()
    {
        var options = Options.CreateBuilder()
            .WithReflectionTools(reflection =>
            {
                reflection.AddRoot("root", new ReflectionRootModel(), new ReflectionRootMetadata("Root"));
            })
            .Build();

        Assert.Equal(
            [
                ReflectionToolIds.ListRoots,
                ReflectionToolIds.InspectObject,
                ReflectionToolIds.DescribeType,
                ReflectionToolIds.SetMemberValue,
                ReflectionToolIds.InvokeMethod
            ],
            options.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public async Task ListReflectionRootsTool_Execute_ReturnsMetadataAndCapabilities()
    {
        var model = new ReflectionRootModel();
        var tool = new ListReflectionRootsTool(
            ReflectionToolsOptions.CreateBuilder()
                .AddRoot(
                    "session",
                    model,
                    new ReflectionRootMetadata("Current Session")
                    {
                        Description = "Session VM",
                        Category = "view-model",
                        Tags = ["debug", "session"],
                        ContainsSensitiveData = true,
                        Attributes = new Dictionary<string, string>
                        {
                            ["team"] = "sdk"
                        }
                    },
                    root => root
                        .AllowWritableMembers("SelectedTab")
                        .AllowInvokableMethods("Rename(System.String)"))
                .Build());

        var result = await tool.Execute(new Dictionary<string, string>());

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<JsonObject>(result.Payload);
        var roots = Assert.IsType<JsonArray>(payload["roots"]);
        var root = Assert.IsType<JsonObject>(Assert.Single(roots));
        var metadata = Assert.IsType<JsonObject>(root["metadata"]);
        var tags = Assert.IsType<JsonArray>(metadata["tags"]);
        var attributes = Assert.IsType<JsonObject>(metadata["attributes"]);

        Assert.Equal("session", root["id"]?.GetValue<string>());
        Assert.Equal("reference", root["registrationKind"]?.GetValue<string>());
        Assert.Equal("weak", root["referenceStrength"]?.GetValue<string>());
        Assert.True(root["available"]!.GetValue<bool>());
        Assert.True(root["canWriteMembers"]!.GetValue<bool>());
        Assert.True(root["canInvokeMethods"]!.GetValue<bool>());
        Assert.Equal("Current Session", metadata["displayName"]?.GetValue<string>());
        Assert.Contains(tags.Select(node => node!.GetValue<string>()), value => value == "debug");
        Assert.Equal("sdk", attributes["team"]?.GetValue<string>());
    }

    [Fact]
    public async Task ListReflectionRootsTool_Execute_ShowsCollectedWeakRootsAsUnavailable()
    {
        var (tool, weakReference) = CreateWeakRootTool();

        for (var attempt = 0; attempt < 8; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!weakReference.TryGetTarget(out _))
            {
                break;
            }
        }

        Assert.False(weakReference.TryGetTarget(out _));

        var result = await tool.Execute(new Dictionary<string, string>());

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<JsonObject>(result.Payload);
        var roots = Assert.IsType<JsonArray>(payload["roots"]);
        var root = Assert.IsType<JsonObject>(Assert.Single(roots));
        Assert.False(root["available"]!.GetValue<bool>());
        Assert.NotNull(root["resolutionError"]?.GetValue<string>());
    }

    [Fact]
    public async Task ListReflectionRootsTool_Execute_ShowsDelegateResolutionErrors()
    {
        var tool = new ListReflectionRootsTool(
            ReflectionToolsOptions.CreateBuilder()
                .AddRoot(
                    "failing",
                    () => throw new InvalidOperationException("resolver failed"),
                    new ReflectionRootMetadata("Failing Root"))
                .Build());

        var result = await tool.Execute(new Dictionary<string, string>());

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<JsonObject>(result.Payload);
        var roots = Assert.IsType<JsonArray>(payload["roots"]);
        var root = Assert.IsType<JsonObject>(Assert.Single(roots));
        Assert.False(root["available"]!.GetValue<bool>());
        Assert.Contains("resolver failed", root["resolutionError"]?.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectObjectTool_Execute_ExpandsCollectionsAndDictionaries()
    {
        var model = new ReflectionRootModel();
        var tool = new InspectObjectTool(CreateOptions(model));

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session",
            ["path"] = "Items[1]"
        });

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<JsonObject>(result.Payload);
        var snapshot = Assert.IsType<JsonObject>(payload["snapshot"]);
        var members = Assert.IsType<JsonArray>(snapshot["members"]);
        var nameMember = members
            .Select(node => Assert.IsType<JsonObject>(node))
            .Single(node => node["name"]?.GetValue<string>() == "Name");

        Assert.Equal(typeof(ReflectionChildModel).FullName, snapshot["runtimeType"]?.GetValue<string>());
        Assert.Equal("Grace", Assert.IsType<JsonObject>(nameMember["value"])["preview"]?.GetValue<string>());

        var dictionaryResult = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session",
            ["path"] = "ChildrenByKey[\"primary\"]"
        });

        Assert.True(dictionaryResult.IsSuccess);
        var dictionarySnapshot = Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(dictionaryResult.Payload)["snapshot"]);
        Assert.Equal(typeof(ReflectionChildModel).FullName, dictionarySnapshot["runtimeType"]?.GetValue<string>());
    }

    [Fact]
    public async Task InspectObjectTool_Execute_UsesOpaqueSnapshotsForDisallowedTypes()
    {
        var model = new ReflectionRootModel();
        var tool = new InspectObjectTool(
            ReflectionToolsOptions.CreateBuilder()
                .AllowNamespacePrefix("Ansight.UnitTests")
                .AddRoot("session", model, new ReflectionRootMetadata("Session"))
                .Build());

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session"
        });

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(result.Payload)["snapshot"]);
        var members = Assert.IsType<JsonArray>(snapshot["members"]);
        var externalMember = members
            .Select(node => Assert.IsType<JsonObject>(node))
            .Single(node => node["name"]?.GetValue<string>() == "External");
        var externalSnapshot = Assert.IsType<JsonObject>(externalMember["value"]);

        Assert.True(externalSnapshot["opaque"]!.GetValue<bool>());
    }

    [Fact]
    public async Task InspectObjectTool_Execute_HonorsNonPublicVisibility()
    {
        var model = new ReflectionRootModel();
        var tool = new InspectObjectTool(
            ReflectionToolsOptions.CreateBuilder()
                .AddRoot(
                    "session",
                    model,
                    new ReflectionRootMetadata("Session"),
                    root => root.WithMemberVisibility(ReflectionMemberVisibility.PublicAndNonPublic))
                .Build());

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session"
        });

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(result.Payload)["snapshot"]);
        var members = Assert.IsType<JsonArray>(snapshot["members"]);
        Assert.Contains(
            members.Select(node => Assert.IsType<JsonObject>(node)["name"]?.GetValue<string>()),
            name => name == "secretToken");
    }

    [Fact]
    public async Task DescribeTypeTool_Execute_RespectsConfiguredVisibility()
    {
        var tool = new DescribeTypeTool(
            ReflectionToolsOptions.CreateBuilder()
                .WithDefaultMemberVisibility(ReflectionMemberVisibility.PublicAndNonPublic)
                .Build());

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["typeName"] = typeof(ReflectionRootModel).FullName!
        });

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<JsonObject>(result.Payload);
        var members = Assert.IsType<JsonArray>(payload["members"]);
        var methods = Assert.IsType<JsonArray>(payload["methods"]);

        Assert.Contains(
            members.Select(node => Assert.IsType<JsonObject>(node)["name"]?.GetValue<string>()),
            name => name == "secretToken");
        Assert.Contains(
            methods.Select(node => Assert.IsType<JsonObject>(node)["signature"]?.GetValue<string>()),
            signature => signature == "Rename(System.String)");
    }

    [Fact]
    public async Task SetMemberValueTool_Execute_WritesAllowListedMembers()
    {
        var model = new ReflectionRootModel();
        var tool = new SetMemberValueTool(
            ReflectionToolsOptions.CreateBuilder()
                .AddRoot(
                    "session",
                    model,
                    new ReflectionRootMetadata("Session"),
                    root => root.AllowWritableMembers("SelectedTab", "Child.Name"))
                .Build());

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session",
            ["path"] = "Child.Name",
            ["valueJson"] = "\"Updated\""
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated", model.Child.Name);
    }

    [Fact]
    public async Task SetMemberValueTool_Execute_RejectsDisallowedMembers()
    {
        var model = new ReflectionRootModel();
        var tool = new SetMemberValueTool(CreateOptions(model));

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session",
            ["path"] = "SelectedTab",
            ["valueJson"] = "\"details\""
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("not allowed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetMemberValueTool_Execute_RejectsCollectionElementWrites()
    {
        var model = new ReflectionRootModel();
        var tool = new SetMemberValueTool(
            ReflectionToolsOptions.CreateBuilder()
                .AddRoot(
                    "session",
                    model,
                    new ReflectionRootMetadata("Session"),
                    root => root.AllowWritableMembers("Items[0]"))
                .Build());

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session",
            ["path"] = "Items[0]",
            ["valueJson"] = "\"blocked\""
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("must end on a field or property", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeMethodTool_Execute_InvokesAllowListedNestedMethods()
    {
        var model = new ReflectionRootModel();
        var tool = new InvokeMethodTool(
            ReflectionToolsOptions.CreateBuilder()
                .AddRoot(
                    "session",
                    model,
                    new ReflectionRootMetadata("Session"),
                    root => root.AllowInvokableMethods("Child#Rename(System.String)"))
                .Build());

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session",
            ["targetPath"] = "Child",
            ["method"] = "Rename",
            ["parameterTypesJson"] = "[\"System.String\"]",
            ["argumentsJson"] = "[\"Updated Child\"]"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Child", model.Child.Name);
        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal("Rename(System.String)", payload["signature"]?.GetValue<string>());
    }

    [Fact]
    public async Task InvokeMethodTool_Execute_RejectsDisallowedMethods()
    {
        var model = new ReflectionRootModel();
        var tool = new InvokeMethodTool(CreateOptions(model));

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session",
            ["method"] = "Rename",
            ["parameterTypesJson"] = "[\"System.String\"]",
            ["argumentsJson"] = "[\"details\"]"
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("not allowed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeMethodTool_Execute_RequiresOverloadDisambiguation()
    {
        var model = new ReflectionRootModel();
        var tool = new InvokeMethodTool(
            ReflectionToolsOptions.CreateBuilder()
                .AddRoot(
                    "session",
                    model,
                    new ReflectionRootMetadata("Session"),
                    root => root.AllowInvokableMethods("Overload(System.String)", "Overload(System.Int32)"))
                .Build());

        var result = await tool.Execute(new Dictionary<string, string>
        {
            ["root"] = "session",
            ["method"] = "Overload"
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("overloaded", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolBridge_Query_And_ListRoots_SerializeReflectionSchemasAndMetadata()
    {
        var options = Options.CreateBuilder()
            .WithReflectionTools(reflection =>
            {
                reflection.AddRoot(
                    "session",
                    new ReflectionRootModel(),
                    new ReflectionRootMetadata("Current Session")
                    {
                        Attributes = new Dictionary<string, string>
                        {
                            ["team"] = "sdk"
                        }
                    });
            })
            .WithReadWriteToolAccess()
            .Build();

        var bridge = options.Tools.CreateBridge(options.ToolGuard);
        var query = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.QueryType,
            Id = "req_1",
            Payload = new JsonObject()
        });

        Assert.Equal(ToolProtocolBridge.CatalogType, query.Type);
        var queryPayload = Assert.IsType<JsonObject>(query.Payload);
        var tools = Assert.IsType<JsonArray>(queryPayload["tools"]);
        Assert.Contains(
            tools.Select(node => Assert.IsType<JsonObject>(node)["id"]?.GetValue<string>()),
            id => id == ReflectionToolIds.ListRoots);

        var call = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.CallType,
            Id = "req_2",
            Payload = new JsonObject
            {
                ["toolId"] = ReflectionToolIds.ListRoots,
                ["arguments"] = new JsonObject()
            }
        });

        Assert.Equal(ToolProtocolBridge.ResultType, call.Type);
        var callPayload = Assert.IsType<JsonObject>(call.Payload);
        var resultPayload = Assert.IsType<JsonObject>(callPayload["result"]);
        var roots = Assert.IsType<JsonArray>(resultPayload["roots"]);
        var root = Assert.IsType<JsonObject>(Assert.Single(roots));
        var metadata = Assert.IsType<JsonObject>(root["metadata"]);
        var attributes = Assert.IsType<JsonObject>(metadata["attributes"]);

        Assert.Equal("Current Session", metadata["displayName"]?.GetValue<string>());
        Assert.Equal("sdk", attributes["team"]?.GetValue<string>());
    }

    private static ReflectionToolsOptions CreateOptions(ReflectionRootModel model)
    {
        return ReflectionToolsOptions.CreateBuilder()
            .AddRoot("session", model, new ReflectionRootMetadata("Session"))
            .Build();
    }

    private static (ListReflectionRootsTool Tool, WeakReference<object> Reference) CreateWeakRootTool()
    {
        var target = new object();
        var reference = new WeakReference<object>(target);
        var tool = new ListReflectionRootsTool(
            ReflectionToolsOptions.CreateBuilder()
                .AddRoot("weak", target, new ReflectionRootMetadata("Weak Root"))
                .Build());

        return (tool, reference);
    }

    public sealed class ReflectionRootModel
    {
        public ReflectionRootModel()
        {
            Child = new ReflectionChildModel("Ada");
            Items =
            [
                new ReflectionChildModel("Ada"),
                new ReflectionChildModel("Grace")
            ];
            ChildrenByKey = new Dictionary<string, ReflectionChildModel>(StringComparer.Ordinal)
            {
                ["primary"] = new ReflectionChildModel("Linus")
            };
            External = new ReflectionToolsExternalTypes.ExternalReflectionModel("sensitive");
        }

        private string secretToken = "shh";

        private string SecretToken => secretToken;

        public ReflectionChildModel Child { get; }

        public List<ReflectionChildModel> Items { get; }

        public Dictionary<string, ReflectionChildModel> ChildrenByKey { get; }

        public ReflectionToolsExternalTypes.ExternalReflectionModel External { get; }

        public string SelectedTab { get; set; } = "overview";

        public void Rename(string value)
        {
            SelectedTab = value;
        }

        public string Overload(string value)
        {
            return "string:" + value;
        }

        public string Overload(int value)
        {
            return "int:" + value;
        }
    }

    public sealed class ReflectionChildModel
    {
        public ReflectionChildModel(string name)
        {
            Name = name;
        }

        public string Name { get; set; }

        public void Rename(string value)
        {
            Name = value;
        }
    }
}
