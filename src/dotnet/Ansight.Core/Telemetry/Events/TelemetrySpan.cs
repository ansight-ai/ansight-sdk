using System.Diagnostics;
using System.Text.Json;

namespace Ansight.Telemetry.Events;

/// <summary>A timed operation (also called a zone). End is idempotent; use Fail for failed operations.</summary>
public sealed class TelemetrySpan : IDisposable
{
    private readonly Action<string, AppEventType, byte, string> emit;
    private readonly long startedTimestamp = Stopwatch.GetTimestamp();
    private readonly string startedAtUtc = DateTimeOffset.UtcNow.ToString("O");
    private int ended;

    internal TelemetrySpan(string name, byte channel, string? parentSpanId,
        Action<string, AppEventType, byte, string> emit)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Span name must not be blank.", nameof(name));
        Id = Guid.NewGuid().ToString("N");
        Name = name.Trim();
        Channel = channel;
        ParentSpanId = parentSpanId;
        this.emit = emit;
        Emit("start", null, null);
    }

    public string Id { get; }
    public string Name { get; }
    public byte Channel { get; }
    public string? ParentSpanId { get; }

    public void End(string status = "ok")
    {
        if (status is not ("ok" or "error" or "cancelled"))
            throw new ArgumentException("Span status must be ok, error, or cancelled.", nameof(status));
        if (Interlocked.Exchange(ref ended, 1) != 0) return;
        Emit("end", Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds, status);
    }

    public void Fail() => End("error");
    public void Dispose() => End();

    private void Emit(string phase, double? durationMs, string? status) => emit(Name, AppEventType.Span, Channel,
        JsonSerializer.Serialize(new { spanId = Id, parentSpanId = ParentSpanId, phase, startedAtUtc, durationMs, status }));
}
