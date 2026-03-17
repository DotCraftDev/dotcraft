using System.Text.Json;
using DotCraft.CLI.Rendering;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;

namespace DotCraft.CLI;

/// <summary>
/// Builds the <c>ServerRequestHandler</c> delegate for <see cref="DotCraft.Protocol.AppServer.AppServerWireClient"/>
/// that handles <c>item/approval/request</c> server-initiated JSON-RPC requests in the CLI.
///
/// The handler parses the approval request params, shows the interactive CLI approval prompt
/// using <see cref="AgentRenderer.ExecuteWhilePausedAsync"/>, and returns the user's decision
/// as the JSON-RPC result object.
/// </summary>
public static class WireApprovalHandler
{
    /// <summary>
    /// Creates an approval handler for <see cref="AppServerWireClient.ServerRequestHandler"/>
    /// that routes <c>item/approval/request</c> to the CLI approval UI.
    ///
    /// The handler suspends the active renderer during the prompt (via
    /// <see cref="AgentRenderer.ExecuteWhilePausedAsync"/>) so the spinner is paused
    /// while the user interacts with the approval dialog.
    /// </summary>
    /// <param name="getRenderer">
    /// Delegate that returns the currently active <see cref="AgentRenderer"/>, or null
    /// if no renderer is running (in which case the approval is auto-declined).
    /// </param>
    public static Func<JsonDocument, Task<object?>> Create(Func<AgentRenderer?> getRenderer)
    {
        return async doc =>
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var m) || m.GetString() != AppServerMethods.ItemApprovalRequest)
                return new { decision = "decline" };

            if (!root.TryGetProperty("params", out var @params))
                return new { decision = "decline" };

            var approvalType = @params.TryGetProperty("approvalType", out var at) ? at.GetString() : null;
            var operation = @params.TryGetProperty("operation", out var op) ? op.GetString() ?? string.Empty : string.Empty;
            var target = @params.TryGetProperty("target", out var tg) ? tg.GetString() ?? string.Empty : string.Empty;

            var renderer = getRenderer();
            if (renderer == null)
                return new { decision = "decline" };

            ApprovalOption choice;
            if (approvalType == "shell")
            {
                choice = await renderer.ExecuteWhilePausedAsync(
                    () => ApprovalPrompt.RequestShellApproval(operation, target));
            }
            else
            {
                choice = await renderer.ExecuteWhilePausedAsync(
                    () => ApprovalPrompt.RequestFileApproval(operation, target));
            }

            var decision = choice switch
            {
                ApprovalOption.Once => "accept",
                ApprovalOption.Session => "acceptForSession",
                ApprovalOption.Always => "acceptAlways",
                _ => "decline"
            };

            return new { decision };
        };
    }
}
