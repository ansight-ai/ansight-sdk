#if ANDROID || IOS || MACCATALYST
namespace Ansight.Maui;

using Microsoft.Maui.Controls;

internal sealed class AnsightMauiPageViewTracker
{
    public AnsightMauiPageViewTracker(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.PageAppearing += HandlePageAppearing;
    }

    private static void HandlePageAppearing(object? sender, Page page)
    {
        if (!Runtime.IsInitialized)
        {
            return;
        }

        Runtime.ScreenViewed(page.GetType().Name);
    }
}
#endif
