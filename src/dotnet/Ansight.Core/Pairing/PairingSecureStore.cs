namespace Ansight.Pairing;

internal interface IPairingSecureStore
{
    bool TryGet(string key, out string? value);

    void Set(string key, string value);

    void Remove(string key);
}

internal static class PairingSecureStoreFactory
{
    public static IPairingSecureStore Create() => new PlatformPairingSecureStore();
}
