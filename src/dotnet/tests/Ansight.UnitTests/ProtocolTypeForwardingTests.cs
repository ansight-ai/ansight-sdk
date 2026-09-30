using System.Reflection;
using Ansight.Pairing.Models;
using Ansight.Tools;
using Xunit;

namespace Ansight.UnitTests;

public sealed class ProtocolTypeForwardingTests
{
    [Theory]
    [InlineData("Ansight.AppLifecycleState")]
    [InlineData("Ansight.Pairing.Models.DeviceAppProfile")]
    [InlineData("Ansight.Pairing.Models.PairingConfigDocument")]
    [InlineData("Ansight.Tools.ToolSchema")]
    [InlineData("Ansight.Tools.ToolProtocolEnvelope")]
    public void ExistingCoreTypeNamesResolveToTheSharedProtocolAssembly(string name)
    {
        var core = typeof(Runtime).Assembly;
        var forwarded = core.GetType(name, throwOnError: true)!;
        Assert.Equal("Ansight.Core", core.GetName().Name);
        Assert.Equal("Ansight.Protocol", forwarded.Assembly.GetName().Name);
        Assert.Contains(forwarded, core.GetForwardedTypes());
    }

    [Fact]
    public void SdkToolBridgeRetainsItsPublicMessageIdentifiers()
    {
        Assert.Equal(ToolProtocolMessageTypes.Capability, ToolProtocolBridge.Capability);
        Assert.Equal(ToolProtocolMessageTypes.CatalogSchema, ToolProtocolBridge.CatalogSchema);
        Assert.Equal(ToolProtocolMessageTypes.CallType, ToolProtocolBridge.CallType);
        Assert.Equal(ToolProtocolMessageTypes.ErrorType, ToolProtocolBridge.ErrorType);
    }
}
