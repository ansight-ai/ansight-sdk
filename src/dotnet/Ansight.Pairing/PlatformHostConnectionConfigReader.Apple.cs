#if IOS || MACCATALYST
namespace Ansight;

using AVFoundation;
using CoreAnimation;
using CoreFoundation;
using Foundation;
using UIKit;

internal static class ApplePlatformHostConnectionConfigReader
{
    public static Task<string?> ReadFromQrCodeAsync(
        HostConnectionRequest request,
        CancellationToken cancellationToken)
    {
        return PlatformQrScannerViewController.ScanAsync(request, cancellationToken);
    }

    private static UIViewController GetPresentingViewController()
    {
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIWindowScene windowScene)
            {
                continue;
            }

            var window = windowScene.Windows.FirstOrDefault(x => x.IsKeyWindow)
                ?? windowScene.Windows.FirstOrDefault();
            if (window?.RootViewController is null)
            {
                continue;
            }

            return ResolvePresentedController(window.RootViewController);
        }

#pragma warning disable CA1422
        var keyWindow = UIApplication.SharedApplication.KeyWindow;
#pragma warning restore CA1422
        if (keyWindow?.RootViewController is not null)
        {
            return ResolvePresentedController(keyWindow.RootViewController);
        }

        throw new InvalidOperationException("Pairing UI is unavailable because no active iOS view controller is available.");
    }

    private static UIViewController ResolvePresentedController(UIViewController controller)
    {
        var current = controller;
        while (true)
        {
            if (current.PresentedViewController is not null)
            {
                current = current.PresentedViewController;
                continue;
            }

            if (current is UINavigationController navigationController && navigationController.VisibleViewController is not null)
            {
                current = navigationController.VisibleViewController;
                continue;
            }

            if (current is UITabBarController tabBarController && tabBarController.SelectedViewController is not null)
            {
                current = tabBarController.SelectedViewController;
                continue;
            }

            return current;
        }
    }

    private static Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> action)
    {
        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                completionSource.TrySetResult(await action());
            }
            catch (OperationCanceledException ex)
            {
                completionSource.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        });

        return completionSource.Task;
    }

    private sealed class PlatformQrScannerViewController : UIViewController
    {
        private readonly TaskCompletionSource<string?> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration cancellationRegistration;
        private readonly UILabel statusLabel;
        private readonly UIButton cancelButton;
        private readonly UIView previewContainer;
        private readonly string requestedTitle;

        private AVCaptureSession? session;
        private AVCaptureMetadataOutput? metadataOutput;
        private QrScannerMetadataDelegate? metadataDelegate;
        private DispatchQueue? metadataQueue;
        private AVCaptureVideoPreviewLayer? previewLayer;
        private bool hasAppeared;
        private int completionState;

        private PlatformQrScannerViewController(string requestedTitle, CancellationToken cancellationToken)
        {
            this.requestedTitle = requestedTitle;
            ModalPresentationStyle = UIModalPresentationStyle.FullScreen;

            previewContainer = new UIView
            {
                BackgroundColor = UIColor.Black
            };

            statusLabel = new UILabel
            {
                Text = "Point the camera at an Ansight pairing QR code.",
                TextColor = UIColor.White,
                TextAlignment = UITextAlignment.Center,
                Lines = 0
            };

            cancelButton = UIButton.FromType(UIButtonType.System);
            cancelButton.SetTitle("Cancel", UIControlState.Normal);
            cancelButton.TouchUpInside += CancelButtonOnTouchUpInside;

            cancellationRegistration = cancellationToken.Register(static state =>
            {
                var controller = (PlatformQrScannerViewController)state!;
                UIApplication.SharedApplication.BeginInvokeOnMainThread(controller.Cancel);
            }, this);
        }

        public static Task<string?> ScanAsync(HostConnectionRequest request, CancellationToken cancellationToken)
        {
            return RunOnMainThreadAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var presenter = GetPresentingViewController();
                var controller = new PlatformQrScannerViewController(
                    string.IsNullOrWhiteSpace(request.Title) ? "Scan Pairing QR" : request.Title!,
                    cancellationToken);

                presenter.PresentViewController(controller, true, null);
                return await controller.completionSource.Task;
            });
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            View!.BackgroundColor = UIColor.Black;
            Title = requestedTitle;

            previewContainer.TranslatesAutoresizingMaskIntoConstraints = false;
            statusLabel.TranslatesAutoresizingMaskIntoConstraints = false;
            cancelButton.TranslatesAutoresizingMaskIntoConstraints = false;

            var overlay = new UIView
            {
                BackgroundColor = UIColor.FromRGBA(0, 0, 0, 180),
                TranslatesAutoresizingMaskIntoConstraints = false
            };

            overlay.Layer.CornerRadius = 16;
            overlay.Layer.MasksToBounds = true;

            View.AddSubview(previewContainer);
            View.AddSubview(overlay);
            overlay.AddSubview(statusLabel);
            overlay.AddSubview(cancelButton);

            NSLayoutConstraint.ActivateConstraints(
            [
                previewContainer.TopAnchor.ConstraintEqualTo(View.TopAnchor),
                previewContainer.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
                previewContainer.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
                previewContainer.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),

                overlay.LeadingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.LeadingAnchor, 16),
                overlay.TrailingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TrailingAnchor, -16),
                overlay.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor, -24),

                statusLabel.TopAnchor.ConstraintEqualTo(overlay.TopAnchor, 12),
                statusLabel.LeadingAnchor.ConstraintEqualTo(overlay.LeadingAnchor, 16),
                statusLabel.TrailingAnchor.ConstraintEqualTo(overlay.TrailingAnchor, -16),

                cancelButton.TopAnchor.ConstraintEqualTo(statusLabel.BottomAnchor, 8),
                cancelButton.LeadingAnchor.ConstraintEqualTo(overlay.LeadingAnchor, 16),
                cancelButton.TrailingAnchor.ConstraintEqualTo(overlay.TrailingAnchor, -16),
                cancelButton.BottomAnchor.ConstraintEqualTo(overlay.BottomAnchor, -12)
            ]);
        }

        public override async void ViewDidAppear(bool animated)
        {
            base.ViewDidAppear(animated);

            if (hasAppeared)
            {
                StartRunning();
                return;
            }

            hasAppeared = true;

            try
            {
                var permissionGranted = await EnsureCameraAccessAsync();
                if (!permissionGranted)
                {
                    Complete(
                        null,
                        new InvalidOperationException("QR pairing requires camera permission. Grant camera access before scanning."));
                    return;
                }

                EnsureSession();
                StartRunning();
            }
            catch (Exception ex)
            {
                Complete(null, ex);
            }
        }

        public override void ViewDidDisappear(bool animated)
        {
            StopRunning();
            base.ViewDidDisappear(animated);
        }

        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();
            if (previewLayer is not null)
            {
                previewLayer.Frame = previewContainer.Bounds;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancelButton.TouchUpInside -= CancelButtonOnTouchUpInside;
                metadataOutput?.Dispose();
                metadataDelegate?.Dispose();
                session?.Dispose();
                metadataQueue?.Dispose();
                previewLayer?.Dispose();
                cancellationRegistration.Dispose();
            }

            base.Dispose(disposing);
        }

        private async Task<bool> EnsureCameraAccessAsync()
        {
            var authorizationStatus = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
            if (authorizationStatus == AVAuthorizationStatus.Authorized)
            {
                return true;
            }

            if (authorizationStatus is AVAuthorizationStatus.Denied or AVAuthorizationStatus.Restricted)
            {
                return false;
            }

            return await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
        }

        private void EnsureSession()
        {
            if (session is not null)
            {
                return;
            }

            var device = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
            if (device is null)
            {
                throw new InvalidOperationException("No camera is available on this device.");
            }

            NSError? error;
            var input = AVCaptureDeviceInput.FromDevice(device, out error);
            if (input is null)
            {
                throw new InvalidOperationException(error?.LocalizedDescription ?? "Unable to access the device camera.");
            }

            session = new AVCaptureSession
            {
                SessionPreset = AVCaptureSession.PresetHigh
            };

            if (session.CanAddInput(input))
            {
                session.AddInput(input);
            }

            metadataOutput = new AVCaptureMetadataOutput();
            if (session.CanAddOutput(metadataOutput))
            {
                session.AddOutput(metadataOutput);
            }

            metadataQueue = new DispatchQueue("ai.ansight.pairing.qr.metadata");
            metadataDelegate = new QrScannerMetadataDelegate(this);
            metadataOutput.SetDelegate(metadataDelegate, metadataQueue);
            metadataOutput.MetadataObjectTypes = AVMetadataObjectType.QRCode;

            previewLayer = new AVCaptureVideoPreviewLayer(session)
            {
                VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
                Frame = previewContainer.Bounds
            };

            previewContainer.Layer.AddSublayer(previewLayer);
            previewContainer.SetNeedsLayout();
        }

        private void StartRunning()
        {
            if (session is null || session.Running)
            {
                return;
            }

            Task.Run(() => session.StartRunning());
        }

        private void StopRunning()
        {
            if (session is null || !session.Running)
            {
                return;
            }

            Task.Run(() => session.StopRunning());
        }

        private void HandlePayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return;
            }

            statusLabel.Text = "Pairing code detected.";
            Complete(payload.Trim(), null);
        }

        private void Cancel()
        {
            Complete(null, null);
        }

        private void Complete(string? payload, Exception? error)
        {
            if (Interlocked.Exchange(ref completionState, 1) != 0)
            {
                return;
            }

            StopRunning();
            cancellationRegistration.Dispose();

            DismissViewController(true, null);

            if (error is not null)
            {
                completionSource.TrySetException(error);
                return;
            }

            completionSource.TrySetResult(payload);
        }

        private void CancelButtonOnTouchUpInside(object? sender, EventArgs e)
        {
            Cancel();
        }

        private sealed class QrScannerMetadataDelegate(PlatformQrScannerViewController owner) : AVCaptureMetadataOutputObjectsDelegate
        {
            public override void DidOutputMetadataObjects(
                AVCaptureMetadataOutput output,
                AVMetadataObject[] metadataObjects,
                AVCaptureConnection connection)
            {
                foreach (var metadataObject in metadataObjects)
                {
                    if (metadataObject is AVMetadataMachineReadableCodeObject codeObject &&
                        !string.IsNullOrWhiteSpace(codeObject.StringValue))
                    {
                        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                            owner.HandlePayload(codeObject.StringValue));
                        return;
                    }
                }
            }
        }
    }
}
#endif
