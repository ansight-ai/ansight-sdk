namespace Ansight.Native;

using System.Text.Json.Nodes;

/// <summary>Allows a runtime feature to pass its native configuration to the platform runtime.</summary>
public interface INativeRuntimeOptionsContributor
{
    void ContributeNativeOptions(JsonObject options);
}
