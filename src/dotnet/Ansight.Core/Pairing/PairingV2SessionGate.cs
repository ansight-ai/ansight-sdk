using Ansight.Tools;

namespace Ansight.Pairing;

internal sealed class PairingV2SessionGate
{
    private readonly string sessionId;
    private readonly HashSet<string> allowedScopes;
    private readonly bool allowCritical;
    private readonly HashSet<string> acceptedCallIds = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    public PairingV2SessionGate(PairingV2SessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.SessionId);
        sessionId = context.SessionId;
        allowedScopes = new HashSet<string>(context.Grant.AllowedScopes, StringComparer.Ordinal);
        allowCritical = context.Grant.AllowCritical;
    }

    public bool CanUseTool(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return allowedScopes.Contains(tool.Scope.ToString()) &&
               (tool.Security.Level != ToolSecurityLevel.Critical || allowCritical);
    }

    public bool TryAccept(ToolProtocolEnvelope envelope, out string code, out string message)
    {
        if (!string.Equals(envelope.SessionId, sessionId, StringComparison.Ordinal))
        {
            code = "tool_session_mismatch";
            message = "Tool request is not bound to the authenticated transport session.";
            return false;
        }

        if (!string.Equals(envelope.Type, ToolProtocolBridge.CallType, StringComparison.Ordinal))
        {
            code = string.Empty;
            message = string.Empty;
            return true;
        }

        lock (gate)
        {
            if (!acceptedCallIds.Add(envelope.Id))
            {
                code = "tool_request_replayed";
                message = "Duplicate tool request ids are not executed on a secure session.";
                return false;
            }
        }

        code = string.Empty;
        message = string.Empty;
        return true;
    }
}
