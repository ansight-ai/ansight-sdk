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
            new GetVisualTreeTool(),
            new FindElementsTool(),
            new GetElementTool(),
            new GetBindablePropertyTool(),
            new SetBindablePropertyTool(),
            new ClearBindablePropertyTool(),
            new InflateXamlTool(),
            new AddElementTool(),
            new RemoveElementTool(),
            new SetAppThemeTool(),
            new GetBindingContextTool(),
            new GetBindingsTool(),
            new GetResourceStateTool(),
            new GetNavigationStateTool(),
            new InvokeElementActionTool(),
            new WaitForUiTool(),
            new GetLayoutDiagnosticsTool(),
            new GetHandlerDiagnosticsTool(),
            new InvokeBindingContextCommandTool(),
            new SetBindingContextPropertyTool()
        });
    }
}
