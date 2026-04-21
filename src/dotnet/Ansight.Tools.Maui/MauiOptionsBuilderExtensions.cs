namespace Ansight.Tools.Maui;

using System;

public static class MauiOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithMauiTools(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddTools(new ITool[]
        {
            new GetCurrentPageTool(),
            new GetMauiVisualTreeTool(),
            new FindMauiElementsTool(),
            new GetMauiElementTool(),
            new GetBindablePropertyTool(),
            new SetBindablePropertyTool(),
            new ClearBindablePropertyTool(),
            new GetBindingContextTool(),
            new GetMauiBindingsTool(),
            new GetMauiResourceStateTool(),
            new GetMauiNavigationStateTool(),
            new InvokeMauiElementActionTool(),
            new WaitForMauiUiTool(),
            new GetMauiLayoutDiagnosticsTool(),
            new GetMauiHandlerDiagnosticsTool(),
            new InvokeBindingContextCommandTool(),
            new SetBindingContextPropertyTool()
        });
    }
}
