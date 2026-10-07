namespace Ansight.Annotations;

internal static class AnnotationBrandIcon
{
    internal static byte[]? LoadBytes()
    {
        using var stream = typeof(AnnotationBrandIcon).Assembly.GetManifestResourceStream("Ansight.Annotations.BrandIcon.png");
        if (stream is null)
        {
            return null;
        }

        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
