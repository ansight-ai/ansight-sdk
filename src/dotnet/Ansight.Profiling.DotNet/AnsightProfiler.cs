using System.Threading;

namespace Ansight.Profiling;

/// <summary>
/// Emits lifecycle signals consumed by Ansight's .NET profiling workflow.
/// </summary>
public static class AnsightProfiler
{
    /// <summary>
    /// The EventSource provider used for Ansight startup signals.
    /// </summary>
    public const string EventSourceName = "Ansight-DotNet-Startup";

    /// <summary>
    /// The event emitted when the application declares itself ready.
    /// </summary>
    public const string ApplicationReadyEventName = "StartupComplete";

    private static int applicationReady;

    /// <summary>
    /// Marks the application as ready for interaction. Only the first call emits an event.
    /// </summary>
    public static void ApplicationReady()
    {
        if (Interlocked.Exchange(ref applicationReady, 1) != 0)
        {
            return;
        }

        DotNetProfilingEventSource.Instance.StartupComplete();
    }
}
