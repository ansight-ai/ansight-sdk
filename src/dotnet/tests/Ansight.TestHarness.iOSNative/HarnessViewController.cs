using UIKit;
using Ansight.TestHarness.Native;

namespace Ansight.TestHarness.iOSNative;

internal sealed class HarnessViewController : UIViewController
{
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var rootView = View ?? throw new InvalidOperationException("The view controller view is not available.");
        rootView.BackgroundColor = UIColor.SystemBackground;

        var nativeBindingStatus = new UILabel
        {
            Lines = 0,
            TextAlignment = UITextAlignment.Center
        };

        void UpdateNativeBindingStatus()
        {
            nativeBindingStatus.Text = NativeBindingDiagnostics.GetStatus();
        }

        UIButton BuildRuntimeButton(string text, Action action)
        {
            return BuildButton(text, () =>
            {
                action();
                UpdateNativeBindingStatus();
            });
        }

        UpdateNativeBindingStatus();

        var controls = new UIView[]
        {
            nativeBindingStatus,
            BuildRuntimeButton("Activate", Runtime.Activate),
            BuildRuntimeButton("Deactivate", Runtime.Deactivate),
            BuildRuntimeButton("Enable FPS", Runtime.EnableFramesPerSecond),
            BuildRuntimeButton("Disable FPS", Runtime.DisableFramesPerSecond),
            BuildButton("Trigger .NET GC", TriggerGc),
            BuildRuntimeButton("Create Test Event", () => Runtime.Event("Test Event")),
            BuildRuntimeButton("Clear Data", Runtime.Clear),
        };

        var stack = new UIStackView(controls)
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
