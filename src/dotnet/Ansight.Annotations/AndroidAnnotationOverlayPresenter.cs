#if ANDROID
namespace Ansight.Annotations;

using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

internal static class AndroidAnnotationOverlayPresenter
{
    internal static Task<AnnotationOverlayResult> PresentAsync(
        AnnotationScreenshotSnapshot? screenshot,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        activity ??= ResolveCurrentActivity();
        if (activity is null || activity.IsFinishing || activity.IsDestroyed)
        {
            return Task.FromResult(AnnotationOverlayResult.Unavailable(
                "No foreground Android activity is available for the feedback overlay."));
        }

        var completion = new TaskCompletionSource<AnnotationOverlayResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() => Present(activity, screenshot, completion, cancellationToken));
        return completion.Task;
    }

    private static void Present(
        Activity activity,
        AnnotationScreenshotSnapshot? screenshot,
        TaskCompletionSource<AnnotationOverlayResult> completion,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            return;
        }

        var dialog = new Dialog(activity, Android.Resource.Style.ThemeDeviceDefaultNoActionBarFullscreen);
        var root = new LinearLayout(activity)
        {
            Orientation = Orientation.Vertical
        };
        root.SetBackgroundColor(Color.Rgb(24, 24, 24));

        var cancelButton = CreateIconButton(activity, "✕", "Cancel");
        var saveButton = CreateIconButton(activity, "✓", "Save");

        var bitmap = screenshot is null
            ? null
            : BitmapFactory.DecodeByteArray(screenshot.Bytes, 0, screenshot.Bytes.Length);
        var drawingView = new AnnotationDrawingView(activity, bitmap);
        var canvasContainer = new FrameLayout(activity);
        canvasContainer.SetBackgroundColor(Color.Black);
        canvasContainer.AddView(
            drawingView,
            new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        cancelButton.Background = CreatePanelBackground(activity);
        saveButton.Background = CreatePanelBackground(activity);
        var cancelLayout = new FrameLayout.LayoutParams(
            Dp(activity, 48),
            Dp(activity, 48),
            GravityFlags.Top | GravityFlags.Left)
        {
            LeftMargin = Dp(activity, 12),
            TopMargin = Dp(activity, 12)
        };
        var saveLayout = new FrameLayout.LayoutParams(
            Dp(activity, 48),
            Dp(activity, 48),
            GravityFlags.Top | GravityFlags.Right)
        {
            RightMargin = Dp(activity, 12),
            TopMargin = Dp(activity, 12)
        };
        canvasContainer.AddView(cancelButton, cancelLayout);
        canvasContainer.AddView(saveButton, saveLayout);

        var selectButton = CreateIconButton(activity, "↖", "Select and edit geometry");
        var freeDrawButton = CreateIconButton(activity, "✎", "Free draw");
        var deleteButton = CreateIconButton(activity, "⌫", "Delete selected geometry");
        var drawingActions = new LinearLayout(activity)
        {
            Orientation = Orientation.Vertical
        };
        drawingActions.SetPadding(Dp(activity, 4), Dp(activity, 4), Dp(activity, 4), Dp(activity, 4));
        drawingActions.Background = CreatePanelBackground(activity);
        drawingActions.AddView(selectButton, IconLayout(activity));
        drawingActions.AddView(freeDrawButton, IconLayout(activity));
        drawingActions.AddView(deleteButton, IconLayout(activity));
        var drawingActionsLayout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Top | GravityFlags.Left)
        {
            LeftMargin = Dp(activity, 12),
            TopMargin = Dp(activity, 68)
        };
        canvasContainer.AddView(drawingActions, drawingActionsLayout);

        var undoButton = CreateIconButton(activity, "↶", "Undo");
        undoButton.Background = CreatePanelBackground(activity);
        var undoLayout = new FrameLayout.LayoutParams(
            Dp(activity, 48),
            Dp(activity, 48),
            GravityFlags.Top | GravityFlags.Right)
        {
            RightMargin = Dp(activity, 12),
            TopMargin = Dp(activity, 68)
        };
        canvasContainer.AddView(undoButton, undoLayout);
        root.AddView(canvasContainer, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));

        var feedback = new EditText(activity)
        {
            Hint = "Overall feedback",
            Gravity = GravityFlags.Top | GravityFlags.Start,
            InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagMultiLine,
            ContentDescription = "Overall feedback"
        };
        feedback.SetMinLines(2);
        feedback.SetMaxLines(5);
        feedback.SetTextColor(Color.White);
        feedback.SetHintTextColor(Color.LightGray);
        feedback.SetBackgroundColor(Color.Rgb(48, 48, 48));
        feedback.SetPadding(Dp(activity, 12), Dp(activity, 10), Dp(activity, 12), Dp(activity, 10));
        var feedbackLayout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        feedbackLayout.SetMargins(Dp(activity, 10), Dp(activity, 8), Dp(activity, 10), Dp(activity, 10));
        root.AddView(feedback, feedbackLayout);
        var overallFeedback = string.Empty;
        var updatingText = false;

        void SelectTool(AnnotationDrawingTool tool)
        {
            drawingView.DrawingTool = tool;
            UpdateActionState();
        }

        void UpdateActionState()
        {
            SetToolButtonState(selectButton, drawingView.DrawingTool == AnnotationDrawingTool.Select);
            SetToolButtonState(freeDrawButton, drawingView.DrawingTool == AnnotationDrawingTool.FreeDraw);
            SetActionEnabled(deleteButton, drawingView.CanDelete);
            SetActionEnabled(undoButton, drawingView.CanUndo);
            updatingText = true;
            feedback.Hint = drawingView.CanDelete
                ? "Text for selected geometry"
                : "Overall feedback";
            feedback.ContentDescription = drawingView.CanDelete ? "Selected geometry text" : "Overall feedback";
            var contextualText = drawingView.CanDelete ? drawingView.SelectedText : overallFeedback;
            if (!string.Equals(feedback.Text, contextualText, StringComparison.Ordinal))
            {
                feedback.Text = contextualText;
            }
            updatingText = false;
        }

        drawingView.StateChanged = UpdateActionState;
        feedback.TextChanged += (_, _) =>
        {
            if (updatingText)
            {
                return;
            }

            if (drawingView.CanDelete)
            {
                drawingView.SetSelectedText(feedback.Text);
                return;
            }

            overallFeedback = feedback.Text ?? string.Empty;
        };
        selectButton.Click += (_, _) => SelectTool(AnnotationDrawingTool.Select);
        freeDrawButton.Click += (_, _) => SelectTool(AnnotationDrawingTool.FreeDraw);
        deleteButton.Click += (_, _) => drawingView.DeleteSelected();
        undoButton.Click += (_, _) => drawingView.Undo();
        cancelButton.Click += (_, _) =>
        {
            completion.TrySetResult(AnnotationOverlayResult.Cancelled());
            dialog.Dismiss();
        };
        saveButton.Click += (_, _) =>
        {
            HideKeyboard(activity, feedback);
            var request = new AnnotationCaptureRequest
            {
                Feedback = overallFeedback,
                Shapes = drawingView.GetShapes()
            };
            completion.TrySetResult(AnnotationOverlayResult.Submitted(request));
            dialog.Dismiss();
        };
        UpdateActionState();

        dialog.CancelEvent += (_, _) => completion.TrySetResult(AnnotationOverlayResult.Cancelled());
        dialog.DismissEvent += (_, _) => completion.TrySetResult(AnnotationOverlayResult.Cancelled());
        dialog.SetContentView(root);
        dialog.SetCanceledOnTouchOutside(false);

        var cancellationRegistration = cancellationToken.Register(() =>
        {
            activity.RunOnUiThread(() =>
            {
                completion.TrySetCanceled(cancellationToken);
                dialog.Dismiss();
            });
        });
        _ = completion.Task.ContinueWith(
            _ => cancellationRegistration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        dialog.Show();
    }

    private static Button CreateIconButton(Activity activity, string glyph, string contentDescription)
    {
        var button = new Button(activity)
        {
            Text = glyph,
            TextSize = 24,
            ContentDescription = contentDescription,
            Gravity = GravityFlags.Center
        };
        button.SetMinWidth(0);
        button.SetMinHeight(0);
        button.SetAllCaps(false);
        button.SetTextColor(Color.White);
        button.SetPadding(0, 0, 0, 0);
        button.SetBackgroundColor(Color.Transparent);
        return button;
    }

    private static LinearLayout.LayoutParams IconLayout(Activity activity)
        => new(Dp(activity, 44), Dp(activity, 44));

    private static GradientDrawable CreatePanelBackground(Activity activity)
    {
        var background = new GradientDrawable();
        background.SetColor(Color.Argb(230, 45, 45, 45));
        background.SetCornerRadius(Dp(activity, 12));
        return background;
    }

    private static void SetToolButtonState(Button button, bool selected)
    {
        button.SetBackgroundColor(selected ? Color.Rgb(0, 122, 255) : Color.Transparent);
        button.Selected = selected;
    }

    private static void SetActionEnabled(Button button, bool enabled)
    {
        button.Enabled = enabled;
        button.Alpha = enabled ? 1 : 0.35f;
    }

    private static void HideKeyboard(Activity activity, View view)
    {
        if (activity.GetSystemService(Activity.InputMethodService) is InputMethodManager inputMethodManager)
        {
            inputMethodManager.HideSoftInputFromWindow(view.WindowToken, HideSoftInputFlags.None);
        }
    }

    private static int Dp(Activity activity, int value)
        => (int)Math.Round(value * activity.Resources!.DisplayMetrics!.Density);

    private static Activity? ResolveCurrentActivity()
    {
        foreach (var assemblyName in new[] { "Microsoft.Maui.Essentials", "Microsoft.Maui" })
        {
            try
            {
                var platformType = Type.GetType($"Microsoft.Maui.ApplicationModel.Platform, {assemblyName}", throwOnError: false);
                var property = platformType?.GetProperty(
                    "CurrentActivity",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (property?.GetValue(null) is Activity activity)
                {
                    return activity;
                }
            }
            catch
            {
            }
        }

        return AndroidAnnotationActivityTracker.GetCurrent();
    }

    private sealed class AnnotationDrawingView : View
    {
        private const float HitTargetDp = 16;
        private const float ResizeHandleDp = 7;

        private readonly Bitmap? bitmap;
        private readonly Paint imagePaint = new(PaintFlags.FilterBitmap);
        private readonly Paint shapePaint = new(PaintFlags.AntiAlias)
        {
            Color = Color.Rgb(255, 59, 48),
            StrokeWidth = 6,
            StrokeCap = Paint.Cap.Round,
            StrokeJoin = Paint.Join.Round
        };
        private readonly Paint selectionPaint = new(PaintFlags.AntiAlias)
        {
            Color = Color.Rgb(0, 122, 255),
            StrokeWidth = 3
        };
        private readonly Paint handlePaint = new(PaintFlags.AntiAlias)
        {
            Color = Color.White
        };
        private readonly AnnotationEditorModel editor = new();
        private RectF imageRect = new();

        internal AnnotationDrawingView(Activity activity, Bitmap? bitmap)
            : base(activity)
        {
            this.bitmap = bitmap;
            shapePaint.SetStyle(Paint.Style.Stroke);
            selectionPaint.SetStyle(Paint.Style.Stroke);
            handlePaint.SetStyle(Paint.Style.Fill);
            SetBackgroundColor(Color.Black);
        }

        internal Action? StateChanged { get; set; }

        internal AnnotationDrawingTool DrawingTool
        {
            get => editor.DrawingTool;
            set
            {
                editor.DrawingTool = value;
                Invalidate();
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
            Invalidate();
            StateChanged?.Invoke();
        }

        internal void DeleteSelected()
        {
            editor.DeleteSelected();
            Invalidate();
            StateChanged?.Invoke();
        }

        internal void SetSelectedText(string? text)
        {
            editor.SetSelectedText(text);
            Invalidate();
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            imageRect = ResolveImageRect();
            if (bitmap is not null)
            {
                canvas.DrawBitmap(bitmap, null, imageRect, imagePaint);
            }

            for (var index = 0; index < editor.Shapes.Count; index++)
            {
                DrawShape(canvas, editor.Shapes[index]);
                if (editor.SelectedIndex == index)
                {
                    DrawSelection(canvas, editor.Shapes[index]);
                }
            }

            if (editor.DraftShape is { } draftShape)
            {
                DrawShape(canvas, draftShape);
            }

            if (editor.DraftPath.Count > 1)
            {
                DrawPath(canvas, editor.DraftPath);
            }
        }

        public override bool OnTouchEvent(MotionEvent? motionEvent)
        {
            if (motionEvent is null)
            {
                return false;
            }

            var point = ToNormalized(ClampToImage(new PointF(motionEvent.GetX(), motionEvent.GetY())));
            switch (motionEvent.ActionMasked)
            {
                case MotionEventActions.Down:
                    editor.PointerDown(point, ResolveHitTolerance());
                    break;
                case MotionEventActions.Move:
                    editor.PointerMoved(point);
                    break;
                case MotionEventActions.Up:
                    editor.PointerUp(point);
                    break;
                case MotionEventActions.Cancel:
                    editor.CancelPointer();
                    break;
                default:
                    return true;
            }

            Invalidate();
            StateChanged?.Invoke();
            return true;
        }

        protected override void OnDetachedFromWindow()
        {
            bitmap?.Dispose();
            imagePaint.Dispose();
            shapePaint.Dispose();
            selectionPaint.Dispose();
            handlePaint.Dispose();
            imageRect.Dispose();
            base.OnDetachedFromWindow();
        }

        private RectF ResolveImageRect()
        {
            if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0 || Width <= 0 || Height <= 0)
            {
                return new RectF(0, 0, Width, Height);
            }

            var scale = Math.Min(Width / (float)bitmap.Width, Height / (float)bitmap.Height);
            var renderedWidth = bitmap.Width * scale;
            var renderedHeight = bitmap.Height * scale;
            var left = (Width - renderedWidth) / 2;
            var top = (Height - renderedHeight) / 2;
            return new RectF(left, top, left + renderedWidth, top + renderedHeight);
        }

        private PointF ClampToImage(PointF point)
            => new(
                Math.Clamp(point.X, imageRect.Left, imageRect.Right),
                Math.Clamp(point.Y, imageRect.Top, imageRect.Bottom));

        private AnnotationPoint ToNormalized(PointF point)
            => new(
                (point.X - imageRect.Left) / Math.Max(imageRect.Width(), 1),
                (point.Y - imageRect.Top) / Math.Max(imageRect.Height(), 1));

        private double ResolveHitTolerance()
        {
            var hitTargetPixels = HitTargetDp * (Resources?.DisplayMetrics?.Density ?? 1);
            return Math.Max(hitTargetPixels / Math.Max(imageRect.Width(), 1), hitTargetPixels / Math.Max(imageRect.Height(), 1));
        }

        private void DrawShape(Canvas canvas, AnnotationShape shape)
        {
            if (shape.Kind == AnnotationShapeKind.FreeDraw)
            {
                DrawPath(canvas, shape.Points);
                return;
            }

            var rect = ResolveBounds(shape);
            if (shape.Kind == AnnotationShapeKind.Ellipse)
            {
                canvas.DrawOval(rect, shapePaint);
            }
            else
            {
                canvas.DrawRect(rect, shapePaint);
            }
            rect.Dispose();
        }

        private void DrawPath(Canvas canvas, IReadOnlyList<AnnotationPoint> points)
        {
            if (points.Count < 2)
            {
                return;
            }

            using var path = new Path();
            var first = ToViewPoint(points[0]);
            path.MoveTo(first.X, first.Y);
            first.Dispose();
            for (var index = 1; index < points.Count; index++)
            {
                var point = ToViewPoint(points[index]);
                path.LineTo(point.X, point.Y);
                point.Dispose();
            }
            canvas.DrawPath(path, shapePaint);
        }

        private void DrawSelection(Canvas canvas, AnnotationShape shape)
        {
            using var bounds = ResolveBounds(shape);
            canvas.DrawRect(bounds, selectionPaint);
            var handleRadius = ResizeHandleDp * (Resources?.DisplayMetrics?.Density ?? 1);
            canvas.DrawCircle(bounds.Right, bounds.Bottom, handleRadius, handlePaint);
            canvas.DrawCircle(bounds.Right, bounds.Bottom, handleRadius, selectionPaint);
        }

        private RectF ResolveBounds(AnnotationShape shape)
        {
            var left = imageRect.Left + (float)(shape.X * imageRect.Width());
            var top = imageRect.Top + (float)(shape.Y * imageRect.Height());
            return new RectF(
                left,
                top,
                left + (float)(shape.Width * imageRect.Width()),
                top + (float)(shape.Height * imageRect.Height()));
        }

        private PointF ToViewPoint(AnnotationPoint point)
            => new(
                imageRect.Left + (float)(point.X * imageRect.Width()),
                imageRect.Top + (float)(point.Y * imageRect.Height()));
    }

    private sealed class AndroidAnnotationActivityTracker : Java.Lang.Object, Application.IActivityLifecycleCallbacks
    {
        private static readonly Lock gate = new();
        private static AndroidAnnotationActivityTracker? instance;
        private Activity? currentActivity;

        internal static Activity? GetCurrent()
        {
            EnsureRegistered();
            lock (gate)
            {
                return instance?.currentActivity;
            }
        }

        private static void EnsureRegistered()
        {
            lock (gate)
            {
                if (instance is not null || Application.Context is not Application application)
                {
                    return;
                }

                instance = new AndroidAnnotationActivityTracker();
                application.RegisterActivityLifecycleCallbacks(instance);
            }
        }

        public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) => SetCurrent(activity);

        public void OnActivityDestroyed(Activity activity)
        {
            lock (gate)
            {
                if (ReferenceEquals(currentActivity, activity))
                {
                    currentActivity = null;
                }
            }
        }

        public void OnActivityPaused(Activity activity) { }

        public void OnActivityResumed(Activity activity) => SetCurrent(activity);

        public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }

        public void OnActivityStarted(Activity activity) => SetCurrent(activity);

        public void OnActivityStopped(Activity activity) { }

        private void SetCurrent(Activity activity)
        {
            lock (gate)
            {
                currentActivity = activity;
            }
        }
    }
}
#endif
