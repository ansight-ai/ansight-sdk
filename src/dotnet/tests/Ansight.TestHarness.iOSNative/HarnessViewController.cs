using UIKit;

namespace Ansight.TestHarness.iOSNative;

internal sealed class HarnessViewController : UIViewController
{
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View.BackgroundColor = UIColor.SystemBackground;

        var buttons = new[]
        {
            BuildButton("Present Sheet", () => Runtime.PresentSheet()),
            BuildButton("Present Overlay", () => Runtime.PresentOverlay()),
            BuildButton("Dismiss Overlay", () => Runtime.DismissOverlay()),
            BuildButton("Overlay Top-Left", () => Runtime.PresentOverlay(OverlayPosition.TopLeft)),
            BuildButton("Overlay Top-Right", () => Runtime.PresentOverlay(OverlayPosition.TopRight)),
            BuildButton("Overlay Bottom-Left", () => Runtime.PresentOverlay(OverlayPosition.BottomLeft)),
            BuildButton("Overlay Bottom-Right", () => Runtime.PresentOverlay(OverlayPosition.BottomRight)),
            BuildButton("Annotations: Labels + Icons", () => Runtime.AppEventRenderingBehaviour = AppEventRenderingBehaviour.LabelsAndIcons),
            BuildButton("Annotations: Icons Only", () => Runtime.AppEventRenderingBehaviour = AppEventRenderingBehaviour.IconsOnly),
            BuildButton("Annotations: None", () => Runtime.AppEventRenderingBehaviour = AppEventRenderingBehaviour.None),
            BuildButton("Theme: Light", () => Runtime.ChartTheme = ChartTheme.Light),
            BuildButton("Theme: Dark", () => Runtime.ChartTheme = ChartTheme.Dark),
            BuildButton("Create Test Annotation", () => Runtime.Event("Test Annotation")),
        };

        var stack = new UIStackView(buttons)
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Distribution = UIStackViewDistribution.FillEqually,
            Alignment = UIStackViewAlignment.Fill,
            Frame = View.Bounds,
            Spacing = 8
        };

        stack.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        View.AddSubview(stack);
    }

    private UIButton BuildButton(string text, Action action)
    {
        var button = UIButton.FromType(UIButtonType.System);
        button.SetTitle(text, UIControlState.Normal);
        button.TouchUpInside += (_, _) => action();
        return button;
    }
}
