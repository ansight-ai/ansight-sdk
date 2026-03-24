namespace Ansight.Tools.SecureStorage;

internal sealed class UnsupportedSecureStorageBackend : ISecureStorageBackend
{
    private static PlatformNotSupportedException CreateException()
        => new("Secure storage tools are only supported on Android, iOS, and Mac Catalyst.");

    public SecureStorageValueResult GetValue(string key) => throw CreateException();

    public SecureStorageWriteResult SetValue(string key, string value) => throw CreateException();

    public SecureStorageRemoveResult RemoveKey(string key) => throw CreateException();
}
