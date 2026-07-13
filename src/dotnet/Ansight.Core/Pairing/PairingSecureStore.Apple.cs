#if IOS || MACCATALYST
using Foundation;
using Security;

namespace Ansight.Pairing;

internal sealed class PlatformPairingSecureStore : IPairingSecureStore
{
    private const string Service = "ai.ansight.pairing.v2";

    public bool TryGet(string key, out string? value)
    {
        using var query = CreateRecord(key);
        using var match = SecKeyChain.QueryAsRecord(query, out var status);
        if (status == SecStatusCode.ItemNotFound)
        {
            value = null;
            return false;
        }

        if (status != SecStatusCode.Success || match?.ValueData is null)
        {
            throw new InvalidOperationException($"Unable to read secure pairing credential. Status: {status}.");
        }

        value = NSString.FromData(match.ValueData, NSStringEncoding.UTF8);
        return value is not null;
    }

    public void Set(string key, string value)
    {
        Remove(key);
        using var record = CreateRecord(key);
        record.Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly;
        record.ValueData = NSData.FromString(value, NSStringEncoding.UTF8);
        var status = SecKeyChain.Add(record);
        if (status != SecStatusCode.Success)
        {
            throw new InvalidOperationException($"Unable to store secure pairing credential. Status: {status}.");
        }
    }

    public void Remove(string key)
    {
        using var record = CreateRecord(key);
        var status = SecKeyChain.Remove(record);
        if (status is not SecStatusCode.Success and not SecStatusCode.ItemNotFound)
        {
            throw new InvalidOperationException($"Unable to remove secure pairing credential. Status: {status}.");
        }
    }

    private static SecRecord CreateRecord(string key) => new(SecKind.GenericPassword)
    {
        Account = key,
        Service = Service,
        Label = "Ansight secure pairing credential"
    };
}
#endif
