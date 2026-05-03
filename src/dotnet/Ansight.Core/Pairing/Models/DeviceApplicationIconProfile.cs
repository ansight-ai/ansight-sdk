namespace Ansight.Pairing.Models;

/// <summary>
/// Describes an application icon captured from the installed package or bundle.
/// </summary>
public sealed class DeviceApplicationIconProfile
{
    /// <summary>
    /// Image format code, such as png or jpeg.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// MIME type for the encoded icon bytes.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Width of the encoded icon in pixels, when known.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Height of the encoded icon in pixels, when known.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Number of encoded bytes before base64 transport encoding.
    /// </summary>
    public long? ByteCount { get; set; }

    /// <summary>
    /// Base64-encoded image bytes.
    /// </summary>
    public string? DataBase64 { get; set; }
}
