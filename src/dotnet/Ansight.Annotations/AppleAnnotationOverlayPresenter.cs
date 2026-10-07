#if IOS || MACCATALYST
namespace Ansight.Annotations;

using CoreGraphics;
using Foundation;
using UIKit;

internal static class AppleAnnotationOverlayPresenter
{
    internal static Task<AnnotationOverlayResult> PresentAsync(
        AnnotationScreenshotSnapshot? screenshot,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<AnnotationOverlayResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            var window = GetActiveWindow();
            var presenter = GetTopViewController(window?.RootViewController);
            if (presenter is null)
            {
                completion.TrySetResult(AnnotationOverlayResult.Unavailable(
                    "No foreground Apple view controller is available for the feedback overlay."));
                return;
            }

            var controller = new AnnotationFeedbackViewController(screenshot, completion);
            controller.ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
            var cancellationRegistration = cancellationToken.Register(() =>
            {
                UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    completion.TrySetCanceled(cancellationToken);
                    controller.DismissViewController(animated: true, completionHandler: null);
                });
            });
            _ = completion.Task.ContinueWith(
                _ => cancellationRegistration.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            presenter.PresentViewController(controller, animated: true, completionHandler: null);
        });
        return completion.Task;
    }

    private static UIWindow? GetActiveWindow()
    {
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIWindowScene windowScene)
            {
                continue;
            }

            var activeWindow = windowScene.Windows.FirstOrDefault(window => window.IsKeyWindow)
                               ?? windowScene.Windows.FirstOrDefault(window => !window.Hidden);
            if (activeWindow is not null)
            {
                return activeWindow;
            }
        }

        return null;
    }

    private static UIViewController? GetTopViewController(UIViewController? controller)
    {
        while (controller?.PresentedViewController is not null)
        {
            controller = controller.PresentedViewController;
        }

        return controller switch
        {
            UINavigationController navigationController => GetTopViewController(navigationController.VisibleViewController),
            UITabBarController tabBarController => GetTopViewController(tabBarController.SelectedViewController),
            _ => controller
        };
    }

    private sealed class AnnotationFeedbackViewController : UIViewController
    {
        private readonly AnnotationScreenshotSnapshot? screenshot;
        private readonly TaskCompletionSource<AnnotationOverlayResult> completion;
        private readonly UIImageView screenshotView = new();
        private readonly AppleAnnotationDrawingView drawingView;
        private readonly UITextView feedbackView = new();
        private readonly UILabel feedbackPlaceholder = new();
        private UIButton? selectButton;
        private UIButton? freeDrawButton;
        private UIButton? deleteButton;
        private UIButton? undoButton;
        private UIView? brandHeader;
        private NSLayoutConstraint? brandHeaderHeight;
        private NSLayoutConstraint? feedbackHeight;
        private NSObject? editingBeganObserver;
        private NSObject? editingEndedObserver;
        private string overallFeedback = string.Empty;
        private bool updatingText;
        private bool finished;

        internal AnnotationFeedbackViewController(
            AnnotationScreenshotSnapshot? screenshot,
            TaskCompletionSource<AnnotationOverlayResult> completion)
        {
            this.screenshot = screenshot;
            this.completion = completion;
            drawingView = new AppleAnnotationDrawingView(
                screenshot is null ? CGSize.Empty : new CGSize(screenshot.Width, screenshot.Height));
            drawingView.DrawingTool = AnnotationDrawingTool.FreeDraw;
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.FromRGB(24, 24, 24);

            if (screenshot is not null)
            {
                using var data = NSData.FromArray(screenshot.Bytes);
                screenshotView.Image = UIImage.LoadFromData(data);
            }
            screenshotView.ContentMode = UIViewContentMode.ScaleAspectFit;
            screenshotView.BackgroundColor = UIColor.Black;

            var cancelButton = CreateIconButton("xmark", "Cancel", Cancel);
            var saveButton = CreateIconButton("checkmark", "Save", Submit);
            saveButton.TintColor = UIColor.FromRGB(250, 67, 31);

            var iconBytes = AnnotationBrandIcon.LoadBytes();
            var brandMark = new UIImageView
            {
                Image = iconBytes is null ? null : UIImage.LoadFromData(NSData.FromArray(iconBytes)),
                ContentMode = UIViewContentMode.ScaleAspectFit,
                AccessibilityLabel = "Ansight logo",
                IsAccessibilityElement = true
            };
            var title = new UILabel
            {
                Text = "Ansight Annotation",
                TextColor = UIColor.White,
                Font = UIFont.BoldSystemFontOfSize(17)
            };
            var header = CreateStack(UILayoutConstraintAxis.Horizontal, brandMark, title);
            header.Spacing = 8;
            brandHeader = header;
            brandHeaderHeight = header.HeightAnchor.ConstraintEqualTo(36);
            editingBeganObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                UITextView.TextDidBeginEditingNotification, _ => SetHeaderVisible(false), feedbackView);
            editingEndedObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                UITextView.TextDidEndEditingNotification, _ => SetHeaderVisible(true), feedbackView);

            var canvasContainer = new UIView
            {
                BackgroundColor = UIColor.Black
            };
            canvasContainer.AddSubview(screenshotView);
            canvasContainer.AddSubview(drawingView);

            selectButton = CreateIconButton("cursorarrow", "Select and edit geometry", () => SelectTool(AnnotationDrawingTool.Select));
            freeDrawButton = CreateIconButton("pencil.tip", "Free draw", () => SelectTool(AnnotationDrawingTool.FreeDraw));
            deleteButton = CreateIconButton("trash", "Delete selected geometry", DeleteSelected);
            undoButton = CreateIconButton("arrow.uturn.backward", "Undo", Undo);
            var actions = CreateStack(
                UILayoutConstraintAxis.Horizontal,
                cancelButton, selectButton, freeDrawButton, deleteButton, undoButton, saveButton);
            actions.Distribution = UIStackViewDistribution.EqualSpacing;
            actions.BackgroundColor = UIColor.FromWhiteAlpha(0.12f, 0.9f);
            actions.Layer.CornerRadius = 12;

            feedbackView.BackgroundColor = UIColor.FromRGB(48, 48, 48);
            feedbackView.TextColor = UIColor.White;
            feedbackView.Font = UIFont.SystemFontOfSize(16);
            feedbackView.Layer.CornerRadius = 8;
            feedbackView.TextContainerInset = new UIEdgeInsets(10, 10, 10, 10);
            feedbackView.AccessibilityLabel = "Overall feedback";
            feedbackView.Changed += HandleFeedbackTextChanged;
            feedbackPlaceholder.Text = "Overall feedback";
            feedbackPlaceholder.TextColor = UIColor.LightGray;
            feedbackPlaceholder.Font = UIFont.SystemFontOfSize(16);
            feedbackPlaceholder.UserInteractionEnabled = false;
            feedbackView.AddSubview(feedbackPlaceholder);

            AddSubview(header);
            AddSubview(actions);
            AddSubview(canvasContainer);
            AddSubview(feedbackView);

            foreach (var view in new UIView[]
                     {
                         cancelButton,
                         saveButton,
                         brandMark,
                         title,
                         header,
                         actions,
                         canvasContainer,
                         screenshotView,
                         drawingView,
                         selectButton,
                         freeDrawButton,
                         deleteButton,
                         undoButton,
                         feedbackView,
                         feedbackPlaceholder
                     })
            {
                view.TranslatesAutoresizingMaskIntoConstraints = false;
            }

            var guide = View.SafeAreaLayoutGuide;
            feedbackHeight = feedbackView.HeightAnchor.ConstraintEqualTo(72);
            NSLayoutConstraint.ActivateConstraints([
                header.TopAnchor.ConstraintEqualTo(guide.TopAnchor, 4),
                header.LeadingAnchor.ConstraintEqualTo(guide.LeadingAnchor, 16),
                header.TrailingAnchor.ConstraintLessThanOrEqualTo(guide.TrailingAnchor, -16),
                brandHeaderHeight,
                brandMark.WidthAnchor.ConstraintEqualTo(36),
                brandMark.HeightAnchor.ConstraintEqualTo(36),

                actions.TopAnchor.ConstraintEqualTo(header.BottomAnchor, 4),
                actions.LeadingAnchor.ConstraintEqualTo(guide.LeadingAnchor, 10),
                actions.TrailingAnchor.ConstraintEqualTo(guide.TrailingAnchor, -10),
                actions.HeightAnchor.ConstraintEqualTo(48),

                canvasContainer.TopAnchor.ConstraintEqualTo(actions.BottomAnchor, 4),
                canvasContainer.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
                canvasContainer.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
                canvasContainer.BottomAnchor.ConstraintEqualTo(feedbackView.TopAnchor, -8),

                screenshotView.TopAnchor.ConstraintEqualTo(canvasContainer.TopAnchor),
                screenshotView.LeadingAnchor.ConstraintEqualTo(canvasContainer.LeadingAnchor),
                screenshotView.TrailingAnchor.ConstraintEqualTo(canvasContainer.TrailingAnchor),
                screenshotView.BottomAnchor.ConstraintEqualTo(canvasContainer.BottomAnchor),

                drawingView.TopAnchor.ConstraintEqualTo(canvasContainer.TopAnchor),
                drawingView.LeadingAnchor.ConstraintEqualTo(canvasContainer.LeadingAnchor),
                drawingView.TrailingAnchor.ConstraintEqualTo(canvasContainer.TrailingAnchor),
                drawingView.BottomAnchor.ConstraintEqualTo(canvasContainer.BottomAnchor),

                feedbackView.LeadingAnchor.ConstraintEqualTo(guide.LeadingAnchor, 10),
                feedbackView.TrailingAnchor.ConstraintEqualTo(guide.TrailingAnchor, -10),
                feedbackView.BottomAnchor.ConstraintEqualTo(View.KeyboardLayoutGuide.TopAnchor, -8),
                feedbackHeight,

                feedbackPlaceholder.TopAnchor.ConstraintEqualTo(feedbackView.TopAnchor, 10),
                feedbackPlaceholder.LeadingAnchor.ConstraintEqualTo(feedbackView.LeadingAnchor, 14),
                feedbackPlaceholder.TrailingAnchor.ConstraintLessThanOrEqualTo(feedbackView.TrailingAnchor, -10)
            ]);

            feedbackView.InputAccessoryView = new UIToolbar
            {
                Items = [
                    new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
                    new UIBarButtonItem("Done", UIBarButtonItemStyle.Done, (_, _) => feedbackView.ResignFirstResponder())
                ]
            };
            feedbackView.InputAccessoryView.SizeToFit();

            drawingView.StateChanged = UpdateActionState;
            UpdateActionState();
        }

        public override void ViewDidDisappear(bool animated)
        {
            base.ViewDidDisappear(animated);
            editingBeganObserver?.Dispose();
            editingEndedObserver?.Dispose();
            if (!finished)
            {
                finished = true;
                completion.TrySetResult(AnnotationOverlayResult.Cancelled());
            }
        }

        private void SetHeaderVisible(bool visible)
        {
            if (brandHeaderHeight is null) return;
            brandHeaderHeight.Constant = visible ? 36 : 0;
            if (feedbackHeight is not null) feedbackHeight.Constant = visible ? 72 : 56;
            if (brandHeader is not null) brandHeader.Hidden = !visible;
        }

        private void SelectTool(AnnotationDrawingTool tool)
        {
            drawingView.DrawingTool = tool;
            UpdateActionState();
        }

        private void DeleteSelected()
        {
            drawingView.DeleteSelected();
            UpdateActionState();
        }

        private void Undo()
        {
            drawingView.Undo();
            UpdateActionState();
        }

        private void UpdateActionState()
        {
            SetToolButtonState(selectButton, AnnotationDrawingTool.Select);
            SetToolButtonState(freeDrawButton, AnnotationDrawingTool.FreeDraw);
            SetActionEnabled(deleteButton, drawingView.CanDelete);
            SetActionEnabled(undoButton, drawingView.CanUndo);
            updatingText = true;
            feedbackView.Text = drawingView.CanDelete ? drawingView.SelectedText : overallFeedback;
            feedbackView.AccessibilityLabel = drawingView.CanDelete ? "Selected geometry text" : "Overall feedback";
            feedbackPlaceholder.Text = drawingView.CanDelete ? "Text for selected geometry" : "Overall feedback";
            feedbackPlaceholder.Hidden = !string.IsNullOrEmpty(feedbackView.Text);
            updatingText = false;
        }

        private void HandleFeedbackTextChanged(object? sender, EventArgs args)
        {
            feedbackPlaceholder.Hidden = !string.IsNullOrEmpty(feedbackView.Text);
            if (updatingText)
            {
                return;
            }

            if (drawingView.CanDelete)
            {
                drawingView.SetSelectedText(feedbackView.Text);
                return;
            }

            overallFeedback = feedbackView.Text ?? string.Empty;
        }

        private void SetToolButtonState(UIButton? button, AnnotationDrawingTool tool)
        {
            if (button is null)
            {
                return;
            }

            var isSelected = drawingView.DrawingTool == tool;
            button.BackgroundColor = isSelected ? UIColor.SystemBlue : UIColor.Clear;
            button.Layer.CornerRadius = 8;
            button.AccessibilityTraits = isSelected
                ? UIAccessibilityTrait.Selected | UIAccessibilityTrait.Button
                : UIAccessibilityTrait.Button;
        }

        private static void SetActionEnabled(UIButton? button, bool enabled)
        {
            if (button is null)
            {
                return;
            }

            button.Enabled = enabled;
            button.Alpha = enabled ? 1 : 0.35f;
        }

        private void Submit()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            feedbackView.ResignFirstResponder();
            var request = new AnnotationCaptureRequest
            {
                Feedback = overallFeedback,
                Shapes = drawingView.GetShapes()
            };
            completion.TrySetResult(AnnotationOverlayResult.Submitted(request));
            DismissViewController(animated: true, completionHandler: null);
        }

        private void Cancel()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            completion.TrySetResult(AnnotationOverlayResult.Cancelled());
            DismissViewController(animated: true, completionHandler: null);
        }

        private static UIButton CreateIconButton(string systemName, string accessibilityLabel, Action action)
        {
            var button = UIButton.FromType(UIButtonType.System);
            button.SetImage(UIImage.GetSystemImage(systemName), UIControlState.Normal);
            button.TintColor = UIColor.White;
            button.AccessibilityLabel = accessibilityLabel;
            button.AccessibilityTraits = UIAccessibilityTrait.Button;
            button.TouchUpInside += (_, _) => action();
            button.WidthAnchor.ConstraintEqualTo(44).Active = true;
            button.HeightAnchor.ConstraintEqualTo(44).Active = true;
            return button;
        }

        private static UIStackView CreateStack(UILayoutConstraintAxis axis, params UIView[] views)
        {
            return new UIStackView(views)
            {
                Axis = axis,
                Alignment = UIStackViewAlignment.Center,
                Distribution = UIStackViewDistribution.Fill,
                Spacing = 2
            };
        }

        private void AddSubview(UIView view) => View!.AddSubview(view);
    }

    private sealed class AppleAnnotationDrawingView : UIView
    {
        private const double HitTargetPoints = 16;
        private const double ResizeHandleRadius = 7;

        private readonly CGSize imageSize;
        private readonly AnnotationEditorModel editor = new();

        internal AppleAnnotationDrawingView(CGSize imageSize)
        {
            this.imageSize = imageSize;
            BackgroundColor = UIColor.Clear;
            Opaque = false;
            MultipleTouchEnabled = false;
        }

        internal Action? StateChanged { get; set; }

        internal AnnotationDrawingTool DrawingTool
        {
            get => editor.DrawingTool;
            set
            {
                editor.DrawingTool = value;
                SetNeedsDisplay();
                StateChanged?.Invoke();
            }
        }

        internal bool CanUndo => editor.CanUndo;

        internal bool CanDelete => editor.CanDelete;

        internal string SelectedText => editor.SelectedShape?.Text ?? string.Empty;

        internal IReadOnlyList<AnnotationShape> GetShapes() => editor.Shapes.ToArray();

        internal void Undo()
        {
            editor.Undo();
            SetNeedsDisplay();
            StateChanged?.Invoke();
        }

        internal void DeleteSelected()
        {
            editor.DeleteSelected();
            SetNeedsDisplay();
            StateChanged?.Invoke();
        }

        internal void SetSelectedText(string? text)
        {
            editor.SetSelectedText(text);
            SetNeedsDisplay();
        }

        public override void Draw(CGRect rect)
        {
            base.Draw(rect);
            for (var index = 0; index < editor.Shapes.Count; index++)
            {
                DrawShape(editor.Shapes[index]);
                if (editor.SelectedIndex == index)
                {
                    DrawSelection(editor.Shapes[index]);
                }
            }

            if (editor.DraftShape is { } draftShape)
            {
                DrawShape(draftShape);
            }

            if (editor.DraftPath.Count > 1)
            {
                DrawPath(editor.DraftPath, 3);
            }
        }

        public override void TouchesBegan(NSSet touches, UIEvent? evt)
        {
            var imageRect = GetImageRect();
            editor.PointerDown(
                ToNormalized(ClampToImage(GetPoint(touches)), imageRect),
                ResolveHitTolerance(imageRect));
            SetNeedsDisplay();
            StateChanged?.Invoke();
        }

        public override void TouchesMoved(NSSet touches, UIEvent? evt)
        {
            editor.PointerMoved(ToNormalized(ClampToImage(GetPoint(touches)), GetImageRect()));
            SetNeedsDisplay();
            StateChanged?.Invoke();
        }

        public override void TouchesEnded(NSSet touches, UIEvent? evt)
        {
            editor.PointerUp(ToNormalized(ClampToImage(GetPoint(touches)), GetImageRect()));
            SetNeedsDisplay();
            StateChanged?.Invoke();
        }

        public override void TouchesCancelled(NSSet touches, UIEvent? evt)
        {
            editor.CancelPointer();
            SetNeedsDisplay();
            StateChanged?.Invoke();
        }

        private CGPoint GetPoint(NSSet touches)
            => (touches.AnyObject as UITouch)?.LocationInView(this) ?? CGPoint.Empty;

        private CGRect GetImageRect()
        {
            if (imageSize.Width <= 0 || imageSize.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return Bounds;
            }

            var scale = Math.Min(Bounds.Width / imageSize.Width, Bounds.Height / imageSize.Height);
            var width = imageSize.Width * scale;
            var height = imageSize.Height * scale;
            return new CGRect(
                Bounds.GetMidX() - (width / 2),
                Bounds.GetMidY() - (height / 2),
                width,
                height);
        }

        private CGPoint ClampToImage(CGPoint point)
        {
            var imageRect = GetImageRect();
            return new CGPoint(
                Math.Clamp(point.X, imageRect.Left, imageRect.Right),
                Math.Clamp(point.Y, imageRect.Top, imageRect.Bottom));
        }

        private static AnnotationPoint ToNormalized(CGPoint point, CGRect imageRect)
        {
            return new AnnotationPoint(
                (point.X - imageRect.Left) / Math.Max(imageRect.Width, 1),
                (point.Y - imageRect.Top) / Math.Max(imageRect.Height, 1));
        }

        private static double ResolveHitTolerance(CGRect imageRect)
            => Math.Max(HitTargetPoints / Math.Max(imageRect.Width, 1), HitTargetPoints / Math.Max(imageRect.Height, 1));

        private void DrawShape(AnnotationShape shape)
        {
            if (shape.Kind == AnnotationShapeKind.FreeDraw)
            {
                DrawPath(shape.Points, shape.StrokeWidth);
                return;
            }

            var rect = ResolveBounds(shape);
            UIColor.FromRGB(255, 59, 48).SetStroke();
            var path = shape.Kind == AnnotationShapeKind.Ellipse
                ? UIBezierPath.FromOval(rect)
                : UIBezierPath.FromRect(rect);
            path.LineWidth = (nfloat)shape.StrokeWidth;
            path.Stroke();
        }

        private void DrawPath(IReadOnlyList<AnnotationPoint> points, double strokeWidth)
        {
            if (points.Count < 2)
            {
                return;
            }

            var imageRect = GetImageRect();
            var path = new UIBezierPath
            {
                LineWidth = (nfloat)strokeWidth,
                LineCapStyle = CGLineCap.Round,
                LineJoinStyle = CGLineJoin.Round
            };
            path.MoveTo(ToViewPoint(points[0], imageRect));
            for (var index = 1; index < points.Count; index++)
            {
                path.AddLineTo(ToViewPoint(points[index], imageRect));
            }

            UIColor.FromRGB(255, 59, 48).SetStroke();
            path.Stroke();
        }

        private void DrawSelection(AnnotationShape shape)
        {
            var bounds = ResolveBounds(shape);
            UIColor.SystemBlue.SetStroke();
            var selectionPath = UIBezierPath.FromRect(bounds);
            selectionPath.LineWidth = 1.5f;
            selectionPath.Stroke();

            UIColor.White.SetFill();
            UIColor.SystemBlue.SetStroke();
            var handleBounds = new CGRect(
                bounds.Right - ResizeHandleRadius,
                bounds.Bottom - ResizeHandleRadius,
                ResizeHandleRadius * 2,
                ResizeHandleRadius * 2);
            var handlePath = UIBezierPath.FromOval(handleBounds);
            handlePath.LineWidth = 2;
            handlePath.Fill();
            handlePath.Stroke();
        }

        private CGRect ResolveBounds(AnnotationShape shape)
        {
            var imageRect = GetImageRect();
            return new CGRect(
                imageRect.Left + (shape.X * imageRect.Width),
                imageRect.Top + (shape.Y * imageRect.Height),
                shape.Width * imageRect.Width,
                shape.Height * imageRect.Height);
        }

        private static CGPoint ToViewPoint(AnnotationPoint point, CGRect imageRect)
            => new(
                imageRect.Left + (point.X * imageRect.Width),
                imageRect.Top + (point.Y * imageRect.Height));
    }
}
#endif
