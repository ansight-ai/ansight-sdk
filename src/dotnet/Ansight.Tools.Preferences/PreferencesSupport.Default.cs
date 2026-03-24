namespace Ansight.Tools.Preferences;

internal sealed class UnsupportedPreferencesBackend : IPreferencesBackend
{
    private static PlatformNotSupportedException CreateException()
        => new("Preferences tools are only supported on Android, iOS, and Mac Catalyst.");

    public PreferenceListKeysResult ListKeys(string? store) => throw CreateException();

    public PreferenceValueResult GetValue(string? store, string key) => throw CreateException();

    public PreferenceWriteResult SetValue(string? store, string key, PreferenceValueKind valueKind, string value) => throw CreateException();

    public PreferenceRemoveResult RemoveKey(string? store, string key) => throw CreateException();
}
