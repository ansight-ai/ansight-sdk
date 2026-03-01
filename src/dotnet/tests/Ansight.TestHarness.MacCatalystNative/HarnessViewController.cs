using UIKit;
using Ansight;

namespace Ansight.TestHarness.MacCatalystNative;

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
            Spacing = 8
        };

        var scrollView = new UIScrollView();
        scrollView.TranslatesAutoresizingMaskIntoConstraints = false;
        stack.TranslatesAutoresizingMaskIntoConstraints = false;

        scrollView.AddSubview(stack);
        View.AddSubview(scrollView);

        NSLayoutConstraint.ActivateConstraints(new[]
        {
            scrollView.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor),
            scrollView.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor),
            scrollView.LeadingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.LeadingAnchor),
            scrollView.TrailingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TrailingAnchor),

            stack.TopAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.TopAnchor),
            stack.BottomAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.BottomAnchor),
            stack.LeadingAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.TrailingAnchor),
            stack.WidthAnchor.ConstraintEqualTo(scrollView.FrameLayoutGuide.WidthAnchor)
        });
    }

    private UIButton BuildButton(string text, Action action)
    {
        var button = UIButton.FromType(UIButtonType.System);
        button.SetTitle(text, UIControlState.Normal);
        button.TouchUpInside += (_, _) => action();

        button.BackgroundColor = ToUiColor(Constants.BrandColor);
        button.SetTitleColor(UIColor.White, UIControlState.Normal);
        button.Layer.CornerRadius = 10;
        button.ContentEdgeInsets = new UIEdgeInsets(10, 16, 10, 16);

        return button;
    }

    private static UIColor ToUiColor(Color color)
    {
        return UIColor.FromRGBA(color.RedNormalized, color.GreenNormalized, color.BlueNormalized, color.AlphaNormalized);
    }
}
