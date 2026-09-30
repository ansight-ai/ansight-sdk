namespace Ansight;

/// <summary>
/// The current high-level lifecycle state of the app process.
/// </summary>
public enum AppLifecycleState
{
    Unknown = 0,
    Foreground,
    Background
}

/// <summary>
/// Raised when the current app lifecycle state changes.
/// </summary>
public sealed class AppLifecycleStateChangedEventArgs : EventArgs
{
    public AppLifecycleStateChangedEventArgs(AppLifecycleState state, DateTimeOffset? changedAtUtc)
    {
        State = state;
        ChangedAtUtc = changedAtUtc;
    }

    public AppLifecycleState State { get; }

    public DateTimeOffset? ChangedAtUtc { get; }
}

/// <summary>
/// Optional sink contract for exposing the current app lifecycle state.
/// </summary>
public interface IAppLifecycleStateSource
{
    AppLifecycleState CurrentAppLifecycleState { get; }

    DateTimeOffset? CurrentAppLifecycleStateChangedUtc { get; }

    event EventHandler<AppLifecycleStateChangedEventArgs>? AppLifecycleStateChanged;
}
