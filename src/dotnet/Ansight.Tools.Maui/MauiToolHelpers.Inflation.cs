namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;

internal static partial class MauiToolHelpers
{
    private static readonly Dictionary<string, Element> inflatedElements = new(StringComparer.OrdinalIgnoreCase);

    internal static MauiElementResolution? ResolveElementOrInflated(string nodeId)
    {
        var liveResolution = ResolveElement(nodeId);
        if (liveResolution != null)
        {
            return liveResolution;
        }

        return TryResolveInflatedElement(nodeId, out var inflatedElement) && inflatedElement != null
            ? new MauiElementResolution(inflatedElement, inflatedElement, Array.Empty<Element>())
            : null;
    }

    internal static void RegisterInflatedElement(Element element)
    {
        inflatedElements[GetElementId(element)] = element;
    }

    internal static bool ForgetInflatedElement(Element element)
        => inflatedElements.Remove(GetElementId(element));

    internal static bool TryResolveInflatedElement(string nodeId, out Element? element)
    {
        if (inflatedElements.TryGetValue(nodeId, out element))
        {
            return true;
        }

        element = inflatedElements.Values.FirstOrDefault(candidate => IsElementMatch(candidate, nodeId));
        return element != null;
    }
}
#endif
