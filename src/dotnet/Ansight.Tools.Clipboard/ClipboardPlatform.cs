namespace Ansight.Tools.Clipboard;

internal static class ClipboardPlatform
{
    internal static IClipboardBackend CreateBackend()
    {
#if ANDROID
        return new AndroidClipboardBackend();
#elif IOS || MACCATALYST
        return new AppleClipboardBackend();
#else
        throw new PlatformNotSupportedException();
#endif
    }

    internal static Task<T> RunAsync<T>(Func<T> action)
    {
#if ANDROID
        if (Android.OS.Looper.MyLooper() == Android.OS.Looper.MainLooper) return Task.FromResult(action());
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = new Android.OS.Handler(Android.OS.Looper.MainLooper!).Post(() =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception error) { completion.TrySetException(error); }
        });
        if (!posted)
            completion.TrySetException(new ClipboardException("clipboard_unavailable", "The app main thread is unavailable."));
        return completion.Task;
#elif IOS || MACCATALYST
        if (Foundation.NSThread.IsMain) return Task.FromResult(action());
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIKit.UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task;
#else
        return Task.FromResult(action());
#endif
    }
}
