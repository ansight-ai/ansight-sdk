using System.Text.Json.Nodes;
using Ansight.Tools;
using Ansight.Tools.Database;
using Ansight.Tools.FileSystem;
using Ansight.Tools.Preferences;
using Ansight.Tools.SecureStorage;
using Ansight.Tools.VisualTree;

namespace Ansight.UnitTests;

public sealed class ToolSecurityTests
{
    public static TheoryData<ITool, ToolSecurityLevel, string> BuiltInTools => new()
    {
        { new ListDatabasesTool(), ToolSecurityLevel.Moderate, ToolSecurityImplications.AccessesDatabases },
        { new DescribeSchemaTool(), ToolSecurityLevel.Moderate, ToolSecurityImplications.MetadataDisclosure },
        { new QueryDatabaseTool(), ToolSecurityLevel.High, ToolSecurityImplications.ExportsData },
        { new ListDirectoryTool(), ToolSecurityLevel.Moderate, ToolSecurityImplications.AccessesFileSystem },
        { new ReadFileTool(), ToolSecurityLevel.High, ToolSecurityImplications.ReadsAppData },
        { new DownloadFileTool(), ToolSecurityLevel.High, ToolSecurityImplications.ExportsData },
        { new BeginBinaryDownloadTool(), ToolSecurityLevel.High, ToolSecurityImplications.UsesBinaryTransfer },
        { new ListPreferenceKeysTool(), ToolSecurityLevel.Moderate, ToolSecurityImplications.AccessesPreferences },
        { new GetPreferenceValueTool(), ToolSecurityLevel.High, ToolSecurityImplications.ReadsAppData },
        { new SetPreferenceValueTool(), ToolSecurityLevel.High, ToolSecurityImplications.WritesAppData },
        { new RemovePreferenceKeyTool(), ToolSecurityLevel.High, ToolSecurityImplications.DeletesAppData },
        { new GetSecureStorageValueTool(), ToolSecurityLevel.Critical, ToolSecurityImplications.HandlesSecrets },
        { new SetSecureStorageValueTool(), ToolSecurityLevel.Critical, ToolSecurityImplications.HandlesSecrets },
        { new RemoveSecureStorageKeyTool(), ToolSecurityLevel.Critical, ToolSecurityImplications.HandlesSecrets },
        { new GetVisualTreeTool(), ToolSecurityLevel.High, ToolSecurityImplications.InspectsUi },
        { new GetScreenshotTool(), ToolSecurityLevel.High, ToolSecurityImplications.CapturesScreenshots },
        { new InspectNodeTool(), ToolSecurityLevel.High, ToolSecurityImplications.InspectsUi },
    };

    [Theory]
    [MemberData(nameof(BuiltInTools))]
    public void BuiltInTools_ExposeStructuredSecurityMetadata(
        ITool tool,
        ToolSecurityLevel expectedLevel,
        string expectedImplication)
    {
        Assert.True(tool.Security.IsSpecified);
        Assert.Equal(expectedLevel, tool.Security.Level);
        Assert.Contains(expectedImplication, tool.Security.Implications);
        Assert.Equal(tool.Security, tool.Definition.Security);
        Assert.False(string.IsNullOrWhiteSpace(tool.Security.Summary));
    }

    [Fact]
    public async Task QueryCatalog_EmitsSecurityMetadataForAnnotatedTools()
    {
        var bridge = new ToolRegistry([new GetSecureStorageValueTool()]).CreateBridge(ToolGuard.ReadOnly);

        var response = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.QueryType,
            Id = "req_1",
            Payload = new JsonObject()
        });

        Assert.Equal(ToolProtocolBridge.CatalogType, response.Type);

        var payload = Assert.IsType<JsonObject>(response.Payload);
        var tools = Assert.IsType<JsonArray>(payload["tools"]);
        var tool = Assert.IsType<JsonObject>(Assert.Single(tools));
        var security = Assert.IsType<JsonObject>(tool["security"]);
        var implications = Assert.IsType<JsonArray>(security["implications"]);

        Assert.Equal("Critical", security["level"]?.GetValue<string>());
        Assert.Equal(
            "Reads decrypted secure-storage values that may contain credentials or tokens.",
            security["summary"]?.GetValue<string>());
        Assert.Contains(
            implications.Select(node => node?.GetValue<string>()).Where(value => value != null),
            value => string.Equals(value, ToolSecurityImplications.HandlesSecrets, StringComparison.Ordinal));
    }
}
