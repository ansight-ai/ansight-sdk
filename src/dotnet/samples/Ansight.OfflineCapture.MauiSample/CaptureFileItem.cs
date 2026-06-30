namespace Ansight.OfflineCapture.MauiSample;

public sealed class CaptureFileItem
{
    public required string RelativePath { get; init; }

    public required long Length { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public string LengthText => FormatBytes(Length);

    public string UpdatedText => UpdatedAtUtc.LocalDateTime.ToString("g");

    public static CaptureFileItem FromFile(string sessionDirectory, string path)
    {
        var file = new FileInfo(path);
        return new CaptureFileItem
        {
            RelativePath = Path.GetRelativePath(sessionDirectory, path).Replace('\\', '/'),
            Length = file.Exists ? file.Length : 0,
            UpdatedAtUtc = file.Exists ? file.LastWriteTimeUtc : DateTimeOffset.MinValue
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes} B";
    }
}
