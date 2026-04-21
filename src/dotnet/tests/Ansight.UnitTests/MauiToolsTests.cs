using Ansight.Tools;
using Ansight.Tools.Maui;

namespace Ansight.UnitTests;

public sealed class MauiToolsTests
{
    [Fact]
    public void ResolvePublicInstanceProperty_OnlyResolvesPublicReadableInstanceProperties()
    {
        Assert.NotNull(MauiToolHelpers.ResolvePublicInstanceProperty(typeof(PublicPropertySubject), nameof(PublicPropertySubject.PublicValue)));
        Assert.NotNull(MauiToolHelpers.ResolvePublicInstanceProperty(typeof(PublicPropertySubject), nameof(PublicPropertySubject.PublicReadPrivateSet)));
        Assert.Null(MauiToolHelpers.ResolvePublicInstanceProperty(typeof(PublicPropertySubject), "PrivateValue"));
        Assert.Null(MauiToolHelpers.ResolvePublicInstanceProperty(typeof(PublicPropertySubject), nameof(PublicPropertySubject.StaticValue)));
        Assert.Null(MauiToolHelpers.ResolvePublicInstanceProperty(typeof(PublicPropertySubject), "Item"));
    }

    [Fact]
    public void HasPublicSetter_RejectsPrivateSetters()
    {
        var publicProperty = Assert.IsAssignableFrom<System.Reflection.PropertyInfo>(
            MauiToolHelpers.ResolvePublicInstanceProperty(typeof(PublicPropertySubject), nameof(PublicPropertySubject.PublicValue)));
        var privateSetterProperty = Assert.IsAssignableFrom<System.Reflection.PropertyInfo>(
            MauiToolHelpers.ResolvePublicInstanceProperty(typeof(PublicPropertySubject), nameof(PublicPropertySubject.PublicReadPrivateSet)));

        Assert.True(MauiToolHelpers.HasPublicSetter(publicProperty));
        Assert.False(MauiToolHelpers.HasPublicSetter(privateSetterProperty));
    }

    [Theory]
    [InlineData("Save", "Save")]
    [InlineData("  Continue  ", "Continue")]
    [InlineData("matthew@example.com", MauiToolHelpers.RedactedLabel)]
    [InlineData("access token abc123", MauiToolHelpers.RedactedLabel)]
    [InlineData("4111 1111 1111 1111", MauiToolHelpers.RedactedLabel)]
    public void CreateSafeLabel_RedactsSensitiveLookingText(string value, string expected)
    {
        Assert.Equal(expected, MauiToolHelpers.CreateSafeLabel(value));
    }

    [Fact]
    public void CreateSafeLabel_HandlesEmptyAndSensitiveInputPlaceholders()
    {
        Assert.Null(MauiToolHelpers.CreateSafeLabel("  "));
        Assert.Equal(MauiToolHelpers.RedactedLabel, MauiToolHelpers.CreateInputPlaceholderLabel("Password", isSensitiveInput: true));
    }

    [Theory]
    [InlineData("//orders/details?id=1234567890", "//orders/details")]
    [InlineData("//profile/matthew@example.com", MauiToolHelpers.RedactedLabel)]
    public void CreateSafeNavigationLocation_RemovesQueryValuesAndRedactsSensitiveSegments(string value, string expected)
    {
        Assert.Equal(expected, MauiToolHelpers.CreateSafeNavigationLocation(value));
    }

    [Fact]
    public void WithMauiTools_RegistersExpectedTools()
    {
        var options = Options.CreateBuilder()
            .WithMauiTools()
            .Build();

        Assert.Equal(
            [
                MauiToolIds.GetCurrentPage,
                MauiToolIds.GetVisualTree,
                MauiToolIds.FindElements,
                MauiToolIds.GetElement,
                MauiToolIds.GetBindableProperty,
                MauiToolIds.SetBindableProperty,
                MauiToolIds.ClearBindableProperty,
                MauiToolIds.InflateXaml,
                MauiToolIds.AddElement,
                MauiToolIds.RemoveElement,
                MauiToolIds.SetAppTheme,
                MauiToolIds.GetBindingContext,
                MauiToolIds.GetBindings,
                MauiToolIds.GetResourceState,
                MauiToolIds.GetNavigationState,
                MauiToolIds.InvokeElementAction,
                MauiToolIds.WaitForUi,
                MauiToolIds.GetLayoutDiagnostics,
                MauiToolIds.GetHandlerDiagnostics,
                MauiToolIds.InvokeBindingContextCommand,
                MauiToolIds.SetBindingContextProperty
            ],
            options.Tools.Select(tool => tool.Id));
    }

    [Theory]
    [MemberData(nameof(HostUnsupportedTools))]
    public async Task MauiTool_Execute_ReturnsPlatformUnsupportedOnHost(ITool tool, IReadOnlyDictionary<string, string> arguments)
    {
        var result = await tool.Execute(arguments);

        Assert.False(result.IsSuccess);
        Assert.Equal("maui_platform_unsupported", result.ErrorCode);
    }

    public static TheoryData<ITool, IReadOnlyDictionary<string, string>> HostUnsupportedTools => new()
    {
        { new GetCurrentPageTool(), new Dictionary<string, string>() },
        { new GetVisualTreeTool(), new Dictionary<string, string>() },
        { new FindElementsTool(), new Dictionary<string, string>() },
        {
            new GetElementTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root"
            }
        },
        {
            new GetBindablePropertyTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root",
                ["propertyName"] = "Text"
            }
        },
        {
            new SetBindablePropertyTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root",
                ["propertyName"] = "Text",
                ["valueJson"] = "\"Updated\""
            }
        },
        {
            new ClearBindablePropertyTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root",
                ["propertyName"] = "Text"
            }
        },
        {
            new InflateXamlTool(),
            new Dictionary<string, string>
            {
                ["xaml"] = "<Label xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\" Text=\"Experiment\" />"
            }
        },
        {
            new AddElementTool(),
            new Dictionary<string, string>
            {
                ["parentNodeId"] = "root",
                ["elementNodeId"] = "child"
            }
        },
        {
            new RemoveElementTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "child"
            }
        },
        {
            new SetAppThemeTool(),
            new Dictionary<string, string>
            {
                ["theme"] = "dark"
            }
        },
        {
            new GetBindingContextTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root"
            }
        },
        {
            new GetBindingsTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root"
            }
        },
        { new GetResourceStateTool(), new Dictionary<string, string>() },
        { new GetNavigationStateTool(), new Dictionary<string, string>() },
        {
            new InvokeElementActionTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root",
                ["action"] = "focus"
            }
        },
        {
            new WaitForUiTool(),
            new Dictionary<string, string>
            {
                ["condition"] = "elementExists",
                ["nodeId"] = "root"
            }
        },
        {
            new GetLayoutDiagnosticsTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root"
            }
        },
        {
            new GetHandlerDiagnosticsTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root"
            }
        },
        {
            new InvokeBindingContextCommandTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root",
                ["commandName"] = "SaveCommand"
            }
        },
        {
            new SetBindingContextPropertyTool(),
            new Dictionary<string, string>
            {
                ["nodeId"] = "root",
                ["propertyName"] = "Name",
                ["valueJson"] = "\"Updated\""
            }
        }
    };

    private sealed class PublicPropertySubject
    {
        public string PublicValue { get; set; } = "public";

        public string PublicReadPrivateSet { get; private set; } = "private setter";

        public static string StaticValue { get; set; } = "static";

        private string PrivateValue { get; set; } = "private";

        public string this[int index] => index.ToString();
    }
}
