namespace Ansight.OfflineCapture;

/// <summary>
/// Controls when offline capture should automatically start.
/// </summary>
public enum OfflineCaptureActivationMode
{
    /// <summary>
    /// Offline capture does not start automatically.
    /// </summary>
    Disabled,

    /// <summary>
    /// Offline capture starts now and does not persist to future app sessions.
    /// </summary>
    Immediate,

    /// <summary>
    /// Offline capture starts during the next app session and then disables itself.
    /// </summary>
    NextSessionOnly,

    /// <summary>
    /// Offline capture starts during every app session until disabled.
    /// </summary>
    AlwaysOn
}
