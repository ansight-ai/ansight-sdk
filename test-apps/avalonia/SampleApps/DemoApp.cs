using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace Ansight.Avalonia.Benchmarks.Samples;

public sealed class DemoApp : Application
{
    private readonly TextBlock output = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 16 };

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if ANSIGHT_NETWORK_SAMPLE
        const string title = "Ansight Network Bench";
#else
        const string title = "Ansight Files & Artifacts Bench";
#endif
        var content = new StackPanel { Spacing = 16, Margin = new Thickness(24, 60, 24, 24) };
        content.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
#if ANSIGHT_BENCHMARK_TOOLS
#if ANSIGHT_NETWORK_SAMPLE
        content.Children.Add(new TextBlock { Text = "Requests use the local synthetic benchmark service.", TextWrapping = TextWrapping.Wrap });
        foreach (var scenario in new[] { "json", "echo", "binary", "error" })
        {
            var selected = scenario;
            content.Children.Add(CreateButton($"Request {scenario}", $"network.{scenario}", async () =>
            {
                var state = BenchmarkOptionsBuilderExtensions.Current ?? throw new InvalidOperationException("Fixtures are not initialized.");
                output.Text = (await state.RequestNetwork(selected)).ToJsonString();
            }));
        }
#else
        content.Children.Add(CreateButton("Prepare files and database", "files.prepare", () =>
        {
            output.Text = RequiredState().Prepare().ToJsonString();
            return Task.CompletedTask;
        }));
        content.Children.Add(CreateButton("Read UTF-8 fixture", "files.read", async () =>
        {
            output.Text = await File.ReadAllTextAsync(Path.Combine(RequiredState().RootPath, "fixture.txt"));
        }));
        content.Children.Add(CreateButton("Inspect artifact contents", "files.inspect", () =>
        {
            output.Text = RequiredState().Snapshot().ToJsonString();
            return Task.CompletedTask;
        }));
#endif
#else
        output.Text = "Benchmark fixtures are available in Debug builds.";
#endif
        AutomationProperties.SetAutomationId(output, "benchmark.output");
        content.Children.Add(output);
        var view = new ScrollViewer { Content = content };
        if (ApplicationLifetime is ISingleViewApplicationLifetime mobile)
        {
            mobile.MainView = view;
        }

        base.OnFrameworkInitializationCompleted();
    }

#if ANSIGHT_BENCHMARK_TOOLS
    private static BenchmarkFixtureState RequiredState()
    {
        return BenchmarkOptionsBuilderExtensions.Current ?? throw new InvalidOperationException("Fixtures are not initialized.");
    }
#endif

    private Button CreateButton(string text, string id, Func<Task> action)
    {
        var button = new Button { Content = text, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch, MinHeight = 48 };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += async (_, _) =>
        {
            try
            {
                button.IsEnabled = false;
                await action();
            }
            catch (Exception exception)
            {
                output.Text = exception.Message;
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }
}
