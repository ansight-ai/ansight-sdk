using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

internal sealed record LocationCapturedSessionEvent(string Type, JsonObject Payload);
