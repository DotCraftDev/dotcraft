using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Automations.Protocol;

/// <summary>
/// Handles <c>automation/*</c> Wire Protocol requests.
/// Registered as a nullable dependency in <see cref="AppServerRequestHandler"/>.
/// </summary>
public sealed partial class AutomationsRequestHandler(
    AutomationOrchestrator orchestrator,
    LocalTaskFileStore fileStore) : IAutomationsRequestHandler
{
    // Automation-specific error codes (M6 spec R6.9)
    private const int TaskNotFoundCode = -32001;
    private const int TaskInvalidStatusCode = -32002;
    private const int SourceNotFoundCode = -32003;

    public async Task<object?> HandleTaskListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskListParams>(msg);
        var tasks = await orchestrator.GetAllTasksAsync(ct);

        if (!string.IsNullOrEmpty(p.SourceName))
            tasks = tasks.Where(t =>
                string.Equals(t.SourceName, p.SourceName, StringComparison.OrdinalIgnoreCase)).ToList();

        return new AutomationTaskListResult
        {
            Tasks = tasks.Select(ToWire).ToList()
        };
    }

    public async Task<object?> HandleTaskReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskReadParams>(msg);
        var tasks = await orchestrator.GetAllTasksAsync(ct);
        var task = tasks.FirstOrDefault(t =>
            string.Equals(t.Id, p.TaskId, StringComparison.Ordinal)
            && string.Equals(t.SourceName, p.SourceName, StringComparison.OrdinalIgnoreCase));

        if (task == null)
            throw new AppServerException(TaskNotFoundCode,
                $"Task '{p.TaskId}' not found in source '{p.SourceName}'.");

        return ToWireDetailed(task);
    }

    public Task<object?> HandleTaskCreateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskCreateParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Title))
            throw AppServerErrors.InvalidParams("'title' is required.");

        var taskId = GenerateTaskId(p.Title);
        var taskDir = Path.Combine(fileStore.TasksRoot, taskId);
        Directory.CreateDirectory(taskDir);

        var now = DateTimeOffset.UtcNow.ToString("o");
        var description = p.Description ?? "";
        var approvalPolicy = string.IsNullOrWhiteSpace(p.ApprovalPolicy)
            ? "workspaceScope"
            : p.ApprovalPolicy.Trim();
        var taskMd = $"""
            ---
            id: "{taskId}"
            title: "{EscapeYamlString(p.Title)}"
            status: pending
            created_at: "{now}"
            updated_at: "{now}"
            thread_id: null
            agent_summary: null
            approval_policy: "{EscapeYamlString(approvalPolicy)}"
            ---

            {description}
            """;

        File.WriteAllText(Path.Combine(taskDir, "task.md"), taskMd.TrimStart());

        var workflowContent = string.IsNullOrWhiteSpace(p.WorkflowTemplate)
            ? BuildDefaultWorkflowContent(NormalizeWorkspaceMode(p.WorkspaceMode))
            : p.WorkflowTemplate;
        File.WriteAllText(Path.Combine(taskDir, "workflow.md"), workflowContent);

        return Task.FromResult<object?>(new AutomationTaskCreateResult
        {
            TaskId = taskId,
            TaskDirectory = taskDir
        });
    }

    public async Task<object?> HandleTaskApproveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskApproveParams>(msg);
        try
        {
            await orchestrator.ApproveTaskAsync(p.SourceName, p.TaskId, ct);
        }
        catch (KeyNotFoundException)
        {
            throw new AppServerException(
                p.SourceName.Contains('/') ? SourceNotFoundCode : TaskNotFoundCode,
                $"Task '{p.TaskId}' not found in source '{p.SourceName}'.");
        }
        catch (InvalidOperationException ex)
        {
            throw new AppServerException(TaskInvalidStatusCode, ex.Message);
        }

        return new { ok = true };
    }

    public async Task<object?> HandleTaskRejectAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskRejectParams>(msg);
        try
        {
            await orchestrator.RejectTaskAsync(p.SourceName, p.TaskId, p.Reason, ct);
        }
        catch (KeyNotFoundException)
        {
            throw new AppServerException(
                p.SourceName.Contains('/') ? SourceNotFoundCode : TaskNotFoundCode,
                $"Task '{p.TaskId}' not found in source '{p.SourceName}'.");
        }
        catch (InvalidOperationException ex)
        {
            throw new AppServerException(TaskInvalidStatusCode, ex.Message);
        }

        return new { ok = true };
    }

    public async Task<object?> HandleTaskDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskDeleteParams>(msg);
        try
        {
            await orchestrator.DeleteTaskAsync(p.SourceName, p.TaskId, ct);
        }
        catch (KeyNotFoundException)
        {
            throw new AppServerException(
                p.SourceName.Contains('/') ? SourceNotFoundCode : TaskNotFoundCode,
                $"Task '{p.TaskId}' not found in source '{p.SourceName}'.");
        }
        catch (NotSupportedException ex)
        {
            throw new AppServerException(TaskInvalidStatusCode, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new AppServerException(TaskInvalidStatusCode, ex.Message);
        }

        return new { ok = true };
    }

    #region Helpers

    private static AutomationTaskWire ToWire(AutomationTask task)
    {
        var w = new AutomationTaskWire
        {
            Id = task.Id,
            Title = task.Title,
            Status = StatusToWire(task.Status),
            SourceName = task.SourceName,
            ThreadId = task.ThreadId,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
        if (task is LocalAutomationTask local)
            w.ApprovalPolicy = local.ApprovalPolicy;
        return w;
    }

    private static AutomationTaskWire ToWireDetailed(AutomationTask task)
    {
        var w = new AutomationTaskWire
        {
            Id = task.Id,
            Title = task.Title,
            Status = StatusToWire(task.Status),
            SourceName = task.SourceName,
            ThreadId = task.ThreadId,
            Description = task.Description,
            AgentSummary = task.AgentSummary,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
        if (task is LocalAutomationTask local)
            w.ApprovalPolicy = local.ApprovalPolicy;
        return w;
    }

    /// <summary>
    /// Converts <see cref="AutomationTaskWire"/> from an <see cref="AutomationTask"/>
    /// for use in <c>automation/task/updated</c> notifications.
    /// </summary>
    public static AutomationTaskWire ToNotificationWire(AutomationTask task) => ToWire(task);

    private static string StatusToWire(AutomationTaskStatus status) => status switch
    {
        AutomationTaskStatus.Pending => "pending",
        AutomationTaskStatus.Dispatched => "dispatched",
        AutomationTaskStatus.AgentRunning => "agent_running",
        AutomationTaskStatus.AgentCompleted => "agent_completed",
        AutomationTaskStatus.AwaitingReview => "awaiting_review",
        AutomationTaskStatus.Approved => "approved",
        AutomationTaskStatus.Rejected => "rejected",
        AutomationTaskStatus.Failed => "failed",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string GenerateTaskId(string title)
    {
        var slug = SlugRegex().Replace(title.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        if (string.IsNullOrEmpty(slug)) slug = "task";
        return $"{slug}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private static string EscapeYamlString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static T GetParams<T>(AppServerIncomingMessage msg) where T : new()
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind == JsonValueKind.Null)
            return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(
                msg.Params.Value.GetRawText(),
                SessionWireJsonOptions.Default) ?? new T();
        }
        catch (JsonException ex)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {ex.Message}");
        }
    }

    /// <summary>
    /// <paramref name="workspaceYamlValue"/> is <c>project</c> or <c>isolated</c> (validated by <see cref="NormalizeWorkspaceMode"/>).
    /// Liquid body uses <c>{{ }}</c>; keep it in a non-interpolated raw string to avoid C# brace escaping.
    /// </summary>
    private static string BuildDefaultWorkflowContent(string workspaceYamlValue)
    {
        const string Body = """

            You are running a local automation task.

            ## Task

            - **ID**: {{ task.id }}
            - **Title**: {{ task.title }}

            ## Instructions

            {{ task.description }}

            When finished, call the **`CompleteLocalTask`** tool with a short summary.
            """;
        return $"""
            ---
            max_rounds: 10
            workspace: {workspaceYamlValue}
            ---
            """ + Body;
    }

    /// <summary>Returns <c>project</c> or <c>isolated</c> for workflow.md YAML.</summary>
    private static string NormalizeWorkspaceMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "project";

        var m = mode.Trim();
        if (string.Equals(m, "project", StringComparison.OrdinalIgnoreCase))
            return "project";
        if (string.Equals(m, "isolated", StringComparison.OrdinalIgnoreCase))
            return "isolated";

        throw AppServerErrors.InvalidParams("'workspaceMode' must be 'project' or 'isolated'.");
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();

    #endregion
}
