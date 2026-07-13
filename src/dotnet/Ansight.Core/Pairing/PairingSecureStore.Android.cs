#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Security.Crypto;

namespace Ansight.Pairing;

internal sealed class PlatformPairingSecureStore : IPairingSecureStore
{
    private const string StoreName = "ai.ansight.pairing.v2";
    private ISharedPreferences? preferences;

    public bool TryGet(string key, out string? value)
    {
        var store = GetPreferences();
        value = store.GetString(key, null);
        return value is not null;
    }

    public void Set(string key, string value)
    {
        using var editor = GetPreferences().Edit();
        editor.PutString(key, value);
        if (!editor.Commit())
        {
            throw new InvalidOperationException("Unable to store secure pairing credential.");
        }
    }

    public void Remove(string key)
    {
        using var editor = GetPreferences().Edit();
        editor.Remove(key);
        if (!editor.Commit())
        {
            throw new InvalidOperationException("Unable to remove secure pairing credential.");
        }
    }

    private ISharedPreferences GetPreferences()
    {
        if (preferences is not null)
        {
            return preferences;
        }

        var context = Application.Context ?? throw new InvalidOperationException("Android application context is unavailable.");
        var masterKey = new MasterKey.Builder(context, StoreName)
            .SetKeyScheme(MasterKey.KeyScheme.Aes256Gcm)
            .Build();
        preferences = EncryptedSharedPreferences.Create(
            context,
            StoreName,
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.Aes256Siv,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.Aes256Gcm);
        return preferences;
    }
}
#endif
