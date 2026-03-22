namespace Ansight;

/// <summary>
/// Categorizes high-level host pairing status summaries.
/// </summary>
public enum HostPairingSummaryKind
{
    RuntimeUnavailable = 0,
    RuntimeInactive = 1,
    DisconnectedNoProfiles = 2,
    DisconnectedCachedProfileAvailable = 3,
    DisconnectedStoredProfileAvailable = 4,
    DisconnectedBundledProfileAvailable = 5,
    DisconnectedMultipleProfilesAvailable = 6,
    Connecting = 7,
    Connected = 8
}
