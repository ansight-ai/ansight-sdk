using Android.App;
using Android.OS;
using Android.Widget;
using Ansight.TestHarness.Native;

namespace Ansight.TestHarness.AndroidNative;

[Activity(Label = "Ansight Android Harness", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var options = Options.CreateBuilder()
            .WithAnsightSdk()
            .WithHostAutoProbe(new HostAutoProbeOptions
            {
                InitialDelay = TimeSpan.FromSeconds(1),
                ProbeInterval = TimeSpan.FromSeconds(5),
                ReconnectDelay = TimeSpan.FromSeconds(10),
                ClientName = "Ansight .NET Android Native Harness"
            })
            .Build();
        EnsureRuntimeStarted(options);

        var layout = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        var nativeBindingStatus = new TextView(this);

        void UpdateNativeBindingStatus()
        {
            var hostStatus = Runtime.HostConnection.Status;
            nativeBindingStatus.Text =
                $"{NativeBindingDiagnostics.GetStatus()}\n" +
                $"Studio: {hostStatus.ConnectionState} • {hostStatus.SummaryMessage}";
        }

        Button BuildRuntimeButton(string text, Action action)
        {
            return BuildButton(text, () =>
            {
                action();
                UpdateNativeBindingStatus();
            });
        }

        UpdateNativeBindingStatus();
        Runtime.HostConnection.StatusChanged += (_, _) =>
            RunOnUiThread(UpdateNativeBindingStatus);
        layout.AddView(nativeBindingStatus);
        layout.AddView(BuildRuntimeButton("Activate", Runtime.Activate));
        layout.AddView(BuildRuntimeButton("Deactivate", Runtime.Deactivate));
        layout.AddView(BuildRuntimeButton("Enable FPS", Runtime.EnableFramesPerSecond));
        layout.AddView(BuildRuntimeButton("Disable FPS", Runtime.DisableFramesPerSecond));
        layout.AddView(BuildButton("Trigger .NET GC", TriggerGc));
        layout.AddView(BuildRuntimeButton("Create Test Event", () => Runtime.Event("Test Event")));
        layout.AddView(BuildRuntimeButton("Clear Data", Runtime.Clear));
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
