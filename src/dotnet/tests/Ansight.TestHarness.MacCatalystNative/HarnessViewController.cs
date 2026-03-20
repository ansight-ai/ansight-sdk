using UIKit;
using Ansight;

namespace Ansight.TestHarness.MacCatalystNative;

internal sealed class HarnessViewController : UIViewController
{
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var rootView = View ?? throw new InvalidOperationException("The view controller view is not available.");
        rootView.BackgroundColor = UIColor.SystemBackground;

        var buttons = new[]
        {
            BuildButton("Activate", Runtime.Activate),
            BuildButton("Deactivate", Runtime.Deactivate),
            BuildButton("Enable FPS", Runtime.EnableFramesPerSecond),
            BuildButton("Disable FPS", Runtime.DisableFramesPerSecond),
            BuildButton("Trigger .NET GC", TriggerGc),
            BuildButton("Create Test Event", () => Runtime.Event("Test Event")),
            BuildButton("Clear Data", Runtime.Clear),
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
        rootView.AddSubview(scrollView);

        NSLayoutConstraint.ActivateConstraints(new[]
        {
            scrollView.TopAnchor.ConstraintEqualTo(rootView.SafeAreaLayoutGuide.TopAnchor),
            scrollView.BottomAnchor.ConstraintEqualTo(rootView.SafeAreaLayoutGuide.BottomAnchor),
            scrollView.LeadingAnchor.ConstraintEqualTo(rootView.SafeAreaLayoutGuide.LeadingAnchor),
            scrollView.TrailingAnchor.ConstraintEqualTo(rootView.SafeAreaLayoutGuide.TrailingAnchor),

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

        button.BackgroundColor = UIColor.FromRGB(250, 67, 31);
        button.SetTitleColor(UIColor.White, UIControlState.Normal);
        button.Layer.CornerRadius = 10;

        return button;
    }

    private static void TriggerGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
