using UIKit;

namespace Ansight.TestHarness.iOSNative;

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
            Frame = View.Bounds,
            Spacing = 8
        };

        stack.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        rootView.AddSubview(stack);
    }

    private UIButton BuildButton(string text, Action action)
    {
        var button = UIButton.FromType(UIButtonType.System);
        button.SetTitle(text, UIControlState.Normal);
        button.TouchUpInside += (_, _) => action();
        return button;
    }

    private static void TriggerGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
