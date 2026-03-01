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
            .WithPresentationWindowProvider(() => this)
            .Build();

        Runtime.InitializeAndActivate(options);

        var layout = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        layout.AddView(BuildButton("Present Sheet", () => Runtime.PresentSheet()));
        layout.AddView(BuildButton("Present Overlay", () => Runtime.PresentOverlay()));
        layout.AddView(BuildButton("Dismiss Overlay", () => Runtime.DismissOverlay()));
        layout.AddView(BuildButton("Overlay Top-Left", () => Runtime.PresentOverlay(OverlayPosition.TopLeft)));
        layout.AddView(BuildButton("Overlay Top-Right", () => Runtime.PresentOverlay(OverlayPosition.TopRight)));
        layout.AddView(BuildButton("Overlay Bottom-Left", () => Runtime.PresentOverlay(OverlayPosition.BottomLeft)));
        layout.AddView(BuildButton("Overlay Bottom-Right", () => Runtime.PresentOverlay(OverlayPosition.BottomRight)));
        layout.AddView(BuildButton("Annotations: Labels + Icons", () => Runtime.AppEventRenderingBehaviour = AppEventRenderingBehaviour.LabelsAndIcons));
        layout.AddView(BuildButton("Annotations: Icons Only", () => Runtime.AppEventRenderingBehaviour = AppEventRenderingBehaviour.IconsOnly));
        layout.AddView(BuildButton("Annotations: None", () => Runtime.AppEventRenderingBehaviour = AppEventRenderingBehaviour.None));
        layout.AddView(BuildButton("Theme: Light", () => Runtime.ChartTheme = ChartTheme.Light));
        layout.AddView(BuildButton("Theme: Dark", () => Runtime.ChartTheme = ChartTheme.Dark));
        layout.AddView(BuildButton("Create Test Annotation", () => Runtime.Event("Test Annotation")));
        SetContentView(layout);
    }

    private Button BuildButton(string text, Action action)
    {
        var button = new Button(this) { Text = text };
        button.Click += (_, _) => action();
        return button;
    }
}
