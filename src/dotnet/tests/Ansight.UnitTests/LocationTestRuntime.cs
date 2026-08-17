using System.Text.Json.Nodes;
using Ansight.Pairing;
using Ansight.Tools;

namespace Ansight.UnitTests;

internal sealed class LocationTestRuntime : IRuntime
{
    public List<LocationCapturedSessionEvent> Events { get; } = [];

    public IDataSink DataSink => null!;

    public ToolProtocolBridge ToolBridge => null!;

    public IHostConnection HostConnection => null!;

    public bool IsActive => false;

    public bool IsFramesPerSecondEnabled => false;

    public bool IsTouchCaptureEnabled => false;

    public event EventHandler? OnActivated;

    public event EventHandler? OnDeactivated;

    public Task<OperationResult> SendSessionEventAsync(
        string type,
        JsonObject payload,
        CancellationToken cancellationToken = default)
    {
        Events.Add(new LocationCapturedSessionEvent(type, payload));
        return Task.FromResult(OperationResult.FromSuccess("sent"));
    }

    public void Activate() => OnActivated?.Invoke(this, EventArgs.Empty);

    public void Deactivate() => OnDeactivated?.Invoke(this, EventArgs.Empty);

    public void EnableFramesPerSecond() { }

    public void DisableFramesPerSecond() { }

    public void EnableTouchCapture() { }

    public void DisableTouchCapture() { }

    public void SetTouchCaptureGuard(Func<bool>? guard) { }

    public void Metric(long value, byte channel) { }

    public void Event(string label) { }

    public void Event(string label, AppEventType type) { }

    public void Event(string label, AppEventType type, string details) { }

    public void Event(string label, byte channel) { }

    public void Event(string label, AppEventType type, byte channel) { }

    public void Event(string label, AppEventType type, byte channel, string details) { }

    public void ScreenViewed(string screenName) { }

    public void ScreenViewed(string screenName, string details) { }

    public void ScreenViewed(string screenName, byte channel) { }

    public void ScreenViewed(string screenName, byte channel, string details) { }

    public void RegisterCustomProperty(string group, string key, object? value) { }

    public bool RemoveCustomProperty(string group, string key) => false;

    public void ClearCustomProperties() { }

    public void Clear() { }
}
