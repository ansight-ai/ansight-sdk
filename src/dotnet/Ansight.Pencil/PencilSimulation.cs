using Ansight.Tools;
#if IOS || MACCATALYST
using Ansight.Pencil.Native.Apple;
#endif

namespace Ansight.Pencil;

public static class PencilSimulation
{
    /// <summary>Whether this platform supports simulation. This does not activate hooks or register tools.</summary>
    public static bool IsSupported
    {
        get
        {
#if IOS || MACCATALYST
            return ANSPencilRuntime.IsSupported;
#else
            return false;
#endif
        }
    }

    internal static IReadOnlyList<ITool> CreateTools()
    {
#if IOS || MACCATALYST
        return IsSupported ? [new SimulatorPenTool(ANSPencilRuntime.Execute)] : [];
#else
        return [];
#endif
    }
}

public static class PencilOptionsExtensions
{
    /// <summary>
    /// Explicitly registers experimental Pencil simulation on iOS Simulator.
    /// Authorizes only Pencil tools. Global disable still wins; hooks wait until playback.
    /// Unsupported platforms register nothing.
    /// </summary>
    public static Options.OptionsBuilder WithSimulatorPenInput(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var tool in PencilSimulation.CreateTools())
        {
            if (!builder.ContainsTool(tool.Id)) builder.AddTool(tool);
            builder.WithToolGrant(tool.Id, ToolPolicy.Write);
        }
        return builder;
    }
}
