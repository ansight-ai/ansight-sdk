#if ANDROID || IOS || MACCATALYST
namespace Ansight.Maui;

using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

internal sealed class AnsightMauiTelemetryInitializer : IMauiInitializeService
{
    private static readonly ConditionalWeakTable<Application, AnsightMauiPageViewTracker> pageViewTrackers = new();

    public void Initialize(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var application = services.GetService(typeof(Microsoft.Maui.IApplication)) as Application
            ?? Application.Current;

        if (application == null)
        {
            return;
        }

        pageViewTrackers.GetValue(application, static app => new AnsightMauiPageViewTracker(app));
    }
}
#endif
