namespace Ansight.Tools.SecureStorage;

#if IOS || MACCATALYST
using Foundation;
using Security;

internal sealed class AppleSecureStorageBackend : ISecureStorageBackend
{
    private readonly string service;

    public AppleSecureStorageBackend(SecureStorageToolsOptions options)
    {
        service = SecureStorageSupport.ResolveAppleService(options);
    }

    public SecureStorageValueResult GetValue(string key)
    {
        using var record = CreateQueryRecord(key);
        using var match = SecKeyChain.QueryAsRecord(record, out var resultCode);

        return resultCode switch
        {
            SecStatusCode.Success => new SecureStorageValueResult(
                service,
                key,
                true,
                NSString.FromData(match!.ValueData, NSStringEncoding.UTF8)),
            SecStatusCode.ItemNotFound => new SecureStorageValueResult(service, key, false, null),
            _ => throw new InvalidOperationException($"Unable to access Keychain item '{key}'. Status: {resultCode}.")
        };
    }

    public SecureStorageWriteResult SetValue(string key, string value)
    {
        using var existingRecord = CreateQueryRecord(key);
        var existingStatus = SecKeyChain.Remove(existingRecord);
        if (existingStatus is not SecStatusCode.Success and not SecStatusCode.ItemNotFound)
        {
            throw new InvalidOperationException($"Unable to replace Keychain item '{key}'. Status: {existingStatus}.");
        }

        using var newRecord = new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = service,
            Label = key,
            Accessible = SecAccessible.AfterFirstUnlock,
            ValueData = NSData.FromString(value, NSStringEncoding.UTF8)
        };

        var addStatus = SecKeyChain.Add(newRecord);
        if (addStatus != SecStatusCode.Success)
        {
            throw new InvalidOperationException($"Unable to store Keychain item '{key}'. Status: {addStatus}.");
        }

        return new SecureStorageWriteResult(service, key, true);
    }

    public SecureStorageRemoveResult RemoveKey(string key)
    {
        using var record = CreateQueryRecord(key);
        var resultCode = SecKeyChain.Remove(record);

        return resultCode switch
        {
            SecStatusCode.Success => new SecureStorageRemoveResult(service, key, true),
            SecStatusCode.ItemNotFound => new SecureStorageRemoveResult(service, key, false),
            _ => throw new InvalidOperationException($"Unable to remove Keychain item '{key}'. Status: {resultCode}.")
        };
    }

    private SecRecord CreateQueryRecord(string key)
    {
        return new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = service
        };
    }
}
#endif
