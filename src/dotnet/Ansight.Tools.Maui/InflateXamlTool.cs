namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
#endif

public sealed class InflateXamlTool : ITool
{
    public string Category => "maui";

    public ToolPolicy Policy => ToolPolicy.Critical;

    public string Id => MauiToolIds.InflateXaml;

    public string Name => "Inflate XAML";

    public string Description => "Inflates arbitrary .NET MAUI XAML into a retained runtime element using LoadFromXaml.";

    public string Keywords => "maui xaml inflate loadfromxaml create control experiment visual tree";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.InflateXamlArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.InflateXamlResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var xaml = GetRequiredString(arguments, "xaml");
            var rootTypeName = GetString(arguments, "rootTypeName");

            if (!TryCreateXamlRoot(xaml, rootTypeName, out var root, out var rootError) || root == null)
            {
                return ToolResult.Failure(rootError ?? "The XAML root could not be created.", errorCode: "maui_xaml_root_create_failed");
            }

            try
            {
                root.LoadFromXaml(xaml);
            }
            catch (Exception exception)
            {
                return ToolResult.Failure(exception.Message, errorCode: "maui_xaml_inflate_failed");
            }

            if (root is not Element element)
            {
                return ToolResult.Failure("The inflated XAML root is not a MAUI Element.", errorCode: "maui_xaml_root_not_element");
            }

            RegisterInflatedElement(element);

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(element),
                ["rootType"] = CreateTypeMetadata(element.GetType()),
                ["registered"] = true,
                ["childCount"] = GetChildElements(element).Count
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
