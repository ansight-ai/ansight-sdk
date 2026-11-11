namespace Ansight.Tools.Database;

internal static class SqliteLibrary
{
#if ANDROID
    // Android does not expose its private system SQLite library to app P/Invoke.
    // The Android package dependency supplies this library for each supported ABI.
    internal const string Name = "e_sqlite3";
#else
    internal const string Name = "sqlite3";
#endif
}
