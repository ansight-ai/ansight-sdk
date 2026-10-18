namespace Ansight;

/// <summary>Keeps crash persistence synchronous and isolated from the original exception path.</summary>
internal sealed class UnhandledExceptionCapture(Action<object, bool> persist)
{
    private int recording;

    public void Record(object? exception, bool fatal)
    {
        if (exception is null || Interlocked.CompareExchange(ref recording, 1, 0) != 0)
            return;

        try
        {
            persist(exception, fatal);
        }
        catch
        {
            // Capture failure must not replace or suppress the application's exception.
        }
        finally
        {
            Volatile.Write(ref recording, 0);
        }
    }

    public void RecordAndroidException(Exception exception, bool handled)
    {
        // A later subscriber or Java caller may handle this JNI-boundary exception.
        // Retain the managed stack now; native exit evidence confirms whether it was fatal.
        if (!handled)
            Record(exception, fatal: false);
    }
}
