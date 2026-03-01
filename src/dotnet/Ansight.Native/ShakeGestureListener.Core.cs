using System;

namespace Ansight;

/// <summary>
/// Shared shake gesture listener surface; platform partials provide sensor wiring and call <see cref="HandleShake"/>.
/// </summary>
internal partial class ShakeGestureListener : IDisposable
{
    private readonly IRuntime runtime;
    private readonly Options options;
    private bool isEnabled;

    public ShakeGestureListener(IRuntime runtime, Options options)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Enable()
    {
        if (isEnabled)
        {
            return;
        }

        if (!options.AllowShakeGesture)
        {
            return;
        }

        if (!EvaluateShakePredicate("enabling shake gesture"))
        {
            Logger.Info("Shake gesture predicate returned false; listener will remain idle until predicate allows it.");
        }

        isEnabled = true;
        OnEnable();
    }

    public void Disable()
    {
        if (!isEnabled)
        {
            return;
        }

        isEnabled = false;
        OnDisable();
    }

    internal void HandleShake()
    {
        if (!isEnabled)
        {
            return;
        }

        if (!EvaluateShakePredicate("processing shake gesture"))
        {
            return;
        }

        switch (options.ShakeGestureBehaviour)
        {
            case ShakeGestureBehaviour.SlideSheet:
                if (runtime.IsSheetPresented)
                {
                    runtime.DismissSheet();
                }
                else
                {
                    runtime.PresentSheet();
                }
                break;

            case ShakeGestureBehaviour.Overlay:
                if (runtime.IsOverlayPresented)
                {
                    runtime.DismissOverlay();
                }
                else
                {
                    runtime.PresentOverlay();
                }
                break;
        }
    }

    private bool EvaluateShakePredicate(string context)
    {
        if (options.ShakeGesturePredicate == null)
        {
            return true;
        }

        try
        {
            return options.ShakeGesturePredicate();
        }
        catch (Exception ex)
        {
            Logger.Error($"Shake gesture predicate threw while {context}.");
            Logger.Exception(ex);
            return false;
        }
    }

    public void Dispose()
    {
        Disable();
    }

    partial void OnEnable();
    partial void OnDisable();
}
