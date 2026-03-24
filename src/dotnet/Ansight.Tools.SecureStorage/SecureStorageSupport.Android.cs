namespace Ansight.Tools.SecureStorage;

#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Security.Crypto;

internal sealed class AndroidSecureStorageBackend : ISecureStorageBackend
{
    private readonly string storeName;
    private ISharedPreferences? sharedPreferences;

    public AndroidSecureStorageBackend(SecureStorageToolsOptions options)
    {
        storeName = SecureStorageSupport.ResolveAndroidStore(options);
    }

    public SecureStorageValueResult GetValue(string key)
    {
        var preferences = GetSharedPreferences();
        var exists = preferences.Contains(key);
        var value = exists ? preferences.GetString(key, null) : null;
        return new SecureStorageValueResult(storeName, key, exists, value);
    }

    public SecureStorageWriteResult SetValue(string key, string value)
    {
        using var editor = GetSharedPreferences().Edit();
        editor.PutString(key, value);
        editor.Apply();
        return new SecureStorageWriteResult(storeName, key, true);
    }

    public SecureStorageRemoveResult RemoveKey(string key)
    {
        var preferences = GetSharedPreferences();
        var removed = preferences.Contains(key);
        if (removed)
        {
            using var editor = preferences.Edit();
            editor.Remove(key);
            editor.Apply();
        }

        return new SecureStorageRemoveResult(storeName, key, removed);
    }

    private ISharedPreferences GetSharedPreferences()
    {
        if (sharedPreferences is not null)
        {
            return sharedPreferences;
        }

        var context = Application.Context ?? throw new InvalidOperationException("Android application context is not available.");

        try
        {
            var masterKey = new MasterKey.Builder(context, storeName)
                .SetKeyScheme(MasterKey.KeyScheme.Aes256Gcm)
                .Build();

            sharedPreferences = EncryptedSharedPreferences.Create(
                context,
                storeName,
                masterKey,
                EncryptedSharedPreferences.PrefKeyEncryptionScheme.Aes256Siv,
                EncryptedSharedPreferences.PrefValueEncryptionScheme.Aes256Gcm);

            return sharedPreferences;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Unable to access the encrypted storage store '{storeName}'.", exception);
        }
    }
}
#endif
