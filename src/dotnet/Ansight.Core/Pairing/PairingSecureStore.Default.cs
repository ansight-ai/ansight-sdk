#if !ANDROID && !IOS && !MACCATALYST
using System.Collections.Concurrent;

namespace Ansight.Pairing;

internal sealed class PlatformPairingSecureStore : IPairingSecureStore
{
    private static readonly ConcurrentDictionary<string, string> values = new(StringComparer.Ordinal);

    public bool TryGet(string key, out string? value) => values.TryGetValue(key, out value);

    public void Set(string key, string value) => values[key] = value;

    public void Remove(string key) => values.TryRemove(key, out _);
}
#endif
