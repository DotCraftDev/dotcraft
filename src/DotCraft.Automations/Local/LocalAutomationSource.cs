using System.Diagnostics;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Automations.Abstractions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations.Local;

/// <summary>
/// File-based local automation tasks under <see cref="LocalTaskFileStore.TasksRoot"/>.
/// </summary>
public sealed class LocalAutomationSource(
    LocalTaskFileStore fileStore,
    LocalWorkflowLoader workflowLoader,
    ILoggerFactory loggerFactory,
    ILogger<LocalAutomationSource> logger)
    : IAutomationSource
{
    // Picks up new task.md files without restarting the host.
    private readonly IDisposable _newTaskWatch =
        fileStore.WatchForNewTasks(t => logger.LogInformation("New local task discovered: {TaskId}", t.Id));

    /// <inheritdoc />
    public string Name => "local";

    /// <inheritdoc />
    public string ToolProfileName => "local-task";

    /// <inheritdoc />
    public void RegisterToolProfile(IToolProfileRegistry registry)
    {
        var completionLogger = loggerFactory.CreateLogger<LocalTaskCompletionToolProvider>();
        IReadOnlyList<IAgentToolProvider> providers =
        [
            new LocalTaskCompletionToolProvider(fileStore, completionLogger)
        ];
        registry.Register(ToolProfileName, providers);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AutomationTask>> GetPendingTasksAsync(CancellationToken ct)
    {
        var all = await fileStore.LoadAllAsync(ct);
        return all.Where(t => t.Status == AutomationTaskStatus.Pending).Cast<AutomationTask>().ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AutomationTask>> GetAllTasksAsync(CancellationToken ct)
    {
        var all = await fileStore.LoadAllAsync(ct);
        return all.Cast<AutomationTask>().ToList();
    }

    /// <inheritdoc />
    public Task<AutomationWorkflowDefinition> GetWorkflowAsync(AutomationTask task, CancellationToken ct) =>
        workflowLoader.LoadAsync((LocalAutomationTask)task, ct);

    /// <inheritdoc />
    public Task OnStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus, CancellationToken ct)
    {
        task.Status = newStatus;
        return fileStore.SaveAsync((LocalAutomationTask)task, ct);
    }

    /// <inheritdoc />
    public Task OnAgentCompletedAsync(AutomationTask task, string agentSummary, CancellationToken ct)
    {
        var local = (LocalAutomationTask)task;
        // Preserve summary from task.md (e.g. CompleteLocalTask) when the turn extract is empty.
        if (!string.IsNullOrWhiteSpace(agentSummary))
            local.AgentSummary = agentSummary;
        return fileStore.SaveAsync(local, ct);
    }

    /// <inheritdoc />
    public async Task<bool> ShouldStopWorkflowAfterTurnAsync(AutomationTask task, CancellationToken ct)
    {
        if (task is not LocalAutomationTask local)
            return false;

        var reloaded = await fileStore.LoadAsync(local.TaskDirectory, ct);
        local.Status = reloaded.Status;
        local.AgentSummary = reloaded.AgentSummary;
        local.ThreadId = reloaded.ThreadId;
        local.Description = reloaded.Description;
        local.CreatedAt = reloaded.CreatedAt;
        local.UpdatedAt = reloaded.UpdatedAt;

        return local.Status == AutomationTaskStatus.AgentCompleted;
    }

    /// <summary>
    /// Approves the task and runs the optional <c>on_approve</c> hook from workflow.md.
    /// </summary>
    public async Task ApproveTaskAsync(string taskId, CancellationToken ct)
    {
        var local = await FindTaskByIdAsync(taskId, ct)
            ?? throw new KeyNotFoundException($"Local task '{taskId}' was not found.");

        if (local.Status != AutomationTaskStatus.AwaitingReview)
            throw new InvalidOperationException(
                $"Task '{taskId}' is not awaiting review (status: {local.Status}).");

        var workflow = await workflowLoader.LoadAsync(local, ct);
        local.Status = AutomationTaskStatus.Approved;
        await fileStore.SaveAsync(local, ct);

        if (!string.IsNullOrWhiteSpace(workflow.OnApprove))
        {
            var workspace = local.AgentWorkspacePath ?? Path.Combine(local.TaskDirectory, "workspace");
            await RunShellHookAsync(workspace, workflow.OnApprove, ct);
        }
    }

    /// <summary>
    /// Rejects the task and runs the optional <c>on_reject</c> hook from workflow.md.
    /// </summary>
    public async Task RejectTaskAsync(string taskId, string? reason, CancellationToken ct)
    {
        var local = await FindTaskByIdAsync(taskId, ct)
            ?? throw new KeyNotFoundException($"Local task '{taskId}' was not found.");

        if (local.Status != AutomationTaskStatus.AwaitingReview)
            throw new InvalidOperationException(
                $"Task '{taskId}' is not awaiting review (status: {local.Status}).");

        var workflow = await workflowLoader.LoadAsync(local, ct);
        local.Status = AutomationTaskStatus.Rejected;
        await fileStore.SaveAsync(local, ct);

        if (!string.IsNullOrWhiteSpace(reason))
            logger.LogInformation("Task {TaskId} rejected: {Reason}", taskId, reason);

        if (!string.IsNullOrWhiteSpace(workflow.OnReject))
        {
            var workspace = local.AgentWorkspacePath ?? Path.Combine(local.TaskDirectory, "workspace");
            await RunShellHookAsync(workspace, workflow.OnReject, ct);
        }
    }

    private async Task<LocalAutomationTask?> FindTaskByIdAsync(string taskId, CancellationToken ct)
    {
        var all = await fileStore.LoadAllAsync(ct);
        return all.FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.Ordinal));
    }

    private async Task RunShellHookAsync(string workingDirectory, string command, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(workingDirectory);
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c " + command;
            }
            else
            {
                psi.FileName = "/bin/sh";
                psi.Arguments = "-c " + command;
            }

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                logger.LogWarning("Failed to start hook process for command: {Command}", command);
                return;
            }

            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                logger.LogWarning("Hook exited with code {Code}: {Err}", proc.ExitCode, err);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hook execution failed for command: {Command}", command);
        }
    }
}
