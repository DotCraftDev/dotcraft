using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Orchestrator;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Automations.Protocol;

/// <summary>
/// Subscribes to <see cref="AutomationOrchestrator.OnTaskStatusChanged"/> and invokes
/// a broadcast callback to push <c>automation/task/updated</c> notifications to all
/// connected Wire Protocol clients.
/// </summary>
public sealed class AutomationsEventDispatcher
{
    private readonly AutomationOrchestrator _orchestrator;
    private readonly Action<AutomationTask, AutomationTaskStatus> _broadcastCallback;

    public AutomationsEventDispatcher(
        AutomationOrchestrator orchestrator,
        Action<AutomationTask, AutomationTaskStatus> broadcastCallback)
    {
        _orchestrator = orchestrator;
        _broadcastCallback = broadcastCallback;
        _orchestrator.OnTaskStatusChanged += OnTaskStatusChangedAsync;
    }

    private Task OnTaskStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus)
    {
        _broadcastCallback(task, newStatus);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the JSON-RPC notification object for <c>automation/task/updated</c>.
    /// </summary>
    public static object BuildNotification(AutomationTask task, string workspacePath) => new
    {
        jsonrpc = "2.0",
        method = AppServerMethods.AutomationTaskUpdated,
        @params = new
        {
            workspacePath,
            task = AutomationsRequestHandler.ToNotificationWire(task)
        }
    };
}
