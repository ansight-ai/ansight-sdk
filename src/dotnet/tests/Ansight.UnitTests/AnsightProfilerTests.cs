using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Ansight.Profiling;

namespace Ansight.UnitTests;

public sealed class AnsightProfilerTests
{
    [Fact]
    public void ApplicationReady_EmitsStartupCompleteOnlyOnce()
    {
        using var listener = new ProfilingEventListener();

        AnsightProfiler.ApplicationReady();
        AnsightProfiler.ApplicationReady();

        Assert.Equal([AnsightProfiler.ApplicationReadyEventName], listener.EventNames);
    }

    private sealed class ProfilingEventListener : EventListener
    {
        private readonly ConcurrentQueue<string> eventNames = new();

        public IReadOnlyCollection<string> EventNames => eventNames.ToArray();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (string.Equals(
                    eventSource.Name,
                    AnsightProfiler.EventSourceName,
                    StringComparison.Ordinal))
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (!string.IsNullOrWhiteSpace(eventData.EventName))
            {
                eventNames.Enqueue(eventData.EventName);
            }
        }
    }
}
