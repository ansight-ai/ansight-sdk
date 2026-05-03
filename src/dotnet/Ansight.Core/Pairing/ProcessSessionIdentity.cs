namespace Ansight.Pairing;

internal static class ProcessSessionIdentity
{
    public static string Current { get; } = Guid.NewGuid().ToString("N");
}
