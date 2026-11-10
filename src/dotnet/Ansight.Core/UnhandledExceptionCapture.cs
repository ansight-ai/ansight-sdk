namespace Ansight;

/// <summary>Keeps crash persistence synchronous and isolated from the original exception path.</summary>
internal sealed class UnhandledExceptionCapture
{
    [ThreadStatic]
    private static bool recording;

    private readonly Action<object, bool> persist;

    public UnhandledExceptionCapture(Action<object, bool> persist)
    {
        this.persist = persist;
    }

    public void Record(object? exception, bool fatal)
    {
        if (exception is null || recording)
        {
            return;
        }

        recording = true;

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
            recording = false;
        }
    }

    public void RecordAndroidException(Exception exception, bool handled)
    {
        // A later subscriber or Java caller may handle this JNI-boundary exception.
        // Retain the managed stack now; native exit evidence confirms whether it was fatal.
        if (!handled)
        {
            Record(exception, fatal: false);
        }
    }
}
