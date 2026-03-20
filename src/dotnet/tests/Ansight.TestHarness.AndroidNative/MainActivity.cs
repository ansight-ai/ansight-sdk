using Android.App;
using Android.OS;
using Android.Widget;

namespace Ansight.TestHarness.AndroidNative;

[Activity(Label = "Ansight Android Harness", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var options = Options.CreateBuilder()
            .WithFramesPerSecond()
            .Build();
        EnsureRuntimeStarted(options);

        var layout = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        layout.AddView(BuildButton("Activate", Runtime.Activate));
        layout.AddView(BuildButton("Deactivate", Runtime.Deactivate));
        layout.AddView(BuildButton("Enable FPS", Runtime.EnableFramesPerSecond));
        layout.AddView(BuildButton("Disable FPS", Runtime.DisableFramesPerSecond));
        layout.AddView(BuildButton("Trigger .NET GC", TriggerGc));
        layout.AddView(BuildButton("Create Test Event", () => Runtime.Event("Test Event")));
        layout.AddView(BuildButton("Clear Data", Runtime.Clear));
        SetContentView(layout);
    }

    private static void EnsureRuntimeStarted(Options options)
    {
        if (!Runtime.IsInitialized)
        {
            Runtime.InitializeAndActivate(options);
            return;
        }

        if (!Runtime.IsActive)
        {
            Runtime.Activate();
        }
    }

    private static void TriggerGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private Button BuildButton(string text, Action action)
    {
        var button = new Button(this) { Text = text };
        button.Click += (_, _) => action();
        return button;
    }
}
