using System.Diagnostics.Tracing;

namespace Ansight.Profiling;

[EventSource(Name = AnsightProfiler.EventSourceName)]
internal sealed class DotNetProfilingEventSource : EventSource
{
    internal static readonly DotNetProfilingEventSource Instance = new();

    private DotNetProfilingEventSource()
    {
    }

    [Event(1, Level = EventLevel.Informational)]
    public void StartupComplete()
    {
        if (IsEnabled())
        {
            WriteEvent(1);
        }
    }
}
