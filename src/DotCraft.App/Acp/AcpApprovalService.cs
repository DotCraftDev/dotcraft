using System.Text.Json;
using DotCraft.Security;

namespace DotCraft.Acp;

/// <summary>
/// ACP-based approval service that sends permission requests to the editor client
/// via the JSON-RPC session/request_permission method.
/// </summary>
public sealed class AcpApprovalService(AcpTransport transport) : IApprovalService
{
    private string _sessionId = "";

    private readonly HashSet<string> _sessionApprovedOps = [];
    private readonly Lock _sessionLock = new();

    // Maps the optionId strings sent in the request back to AcpPermissionKind constants.
    private static readonly Dictionary<string, string> OptionIdToKind = new()
    {
        ["allow-once"]   = AcpPermissionKind.AllowOnce,
        ["allow-always"] = AcpPermissionKind.AllowAlways,
        ["reject-once"]  = AcpPermissionKind.RejectOnce,
    };

    public void SetSessionId(string sessionId)
    {
        _sessionId = sessionId;
    }

    public async Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
    {
        var opKey = $"file:{operation}:{path}";
        lock (_sessionLock)
        {
            if (_sessionApprovedOps.Contains(opKey) || _sessionApprovedOps.Contains($"file:{operation}:*"))
                return true;
        }

        var toolCall = new AcpToolCallInfo
        {
            ToolCallId = Guid.NewGuid().ToString("N")[..12],
            Title = $"File {operation}: {path}",
            Kind = operation.ToLowerInvariant() switch
            {
                "read" or "list" => AcpToolKind.Read,
                "write" or "edit" => AcpToolKind.Edit,
                "delete" => AcpToolKind.Delete,
                _ => AcpToolKind.Other
            },
            Status = AcpToolStatus.Pending
        };

        var kind = await SendPermissionRequestAsync(toolCall);
        if (kind == null) return false;

        switch (kind)
        {
            case AcpPermissionKind.AllowAlways:
                lock (_sessionLock) { _sessionApprovedOps.Add($"file:{operation}:*"); }
                return true;
            case AcpPermissionKind.AllowOnce:
                return true;
            default:
                return false;
        }
    }

    public async Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
    {
        lock (_sessionLock)
        {
            if (_sessionApprovedOps.Contains("shell:*"))
                return true;
        }

        var toolCall = new AcpToolCallInfo
        {
            ToolCallId = Guid.NewGuid().ToString("N")[..12],
            Title = $"Shell: {(command.Length > 80 ? command[..80] + "..." : command)}",
            Kind = AcpToolKind.Execute,
            Status = AcpToolStatus.Pending
        };

        var kind = await SendPermissionRequestAsync(toolCall);
        if (kind == null) return false;

        switch (kind)
        {
            case AcpPermissionKind.AllowAlways:
                lock (_sessionLock) { _sessionApprovedOps.Add("shell:*"); }
                return true;
            case AcpPermissionKind.AllowOnce:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Sends a session/request_permission request and returns the resolved
    /// <see cref="AcpPermissionKind"/> string, or null if cancelled/timed out.
    /// </summary>
    private async Task<string?> SendPermissionRequestAsync(AcpToolCallInfo toolCall)
    {
        var requestParams = new RequestPermissionParams
        {
            SessionId = _sessionId,
            ToolCall = toolCall,
            Options =
            [
                new PermissionOption { OptionId = "allow-once",   Name = "Allow once",   Kind = AcpPermissionKind.AllowOnce   },
                new PermissionOption { OptionId = "allow-always", Name = "Allow always", Kind = AcpPermissionKind.AllowAlways },
                new PermissionOption { OptionId = "reject-once",  Name = "Reject",       Kind = AcpPermissionKind.RejectOnce  },
            ]
        };

        try
        {
            var resultElement = await transport.SendClientRequestAsync(
                AcpMethods.RequestPermission,
                requestParams,
                timeout: TimeSpan.FromSeconds(120));

            var result = resultElement.Deserialize<RequestPermissionResult>();
            if (result == null) return null;

            // "cancelled" outcome means the prompt turn was cancelled.
            if (result.Outcome.Outcome == "cancelled") return null;

            // Map the selected optionId back to a permission kind constant.
            if (result.Outcome.OptionId != null &&
                OptionIdToKind.TryGetValue(result.Outcome.OptionId, out var kind))
                return kind;

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
