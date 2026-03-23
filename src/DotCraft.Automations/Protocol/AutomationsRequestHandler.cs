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
    // Automation-specific error codes are now in AppServerErrors (-32051 to -32054)

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
            throw AppServerErrors.TaskNotFound(p.TaskId, p.SourceName);

        return ToWireDetailed(task);
    }

    public Task<object?> HandleTaskCreateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskCreateParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Title))
            throw AppServerErrors.InvalidParams("'title' is required.");

        // Validate title length
        if (p.Title.Length > 200)
            throw AppServerErrors.InvalidParams("'title' must be 200 characters or less.");

        // Validate description length
        if (p.Description != null && p.Description.Length > 10000)
            throw AppServerErrors.InvalidParams("'description' must be 10000 characters or less.");

        var taskId = GenerateTaskId(p.Title);
        
        // Security: Validate the generated task ID to prevent path traversal
        if (!IsValidTaskId(taskId))
            throw AppServerErrors.InvalidParams("Generated task ID contains invalid characters.");

        var taskDir = Path.Combine(fileStore.TasksRoot, taskId);
        
        // Security: Ensure the task directory is within TasksRoot (path traversal protection)
        var fullTaskDir = Path.GetFullPath(taskDir);
        var fullRoot = Path.GetFullPath(fileStore.TasksRoot);
        if (!fullTaskDir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams("Invalid task directory path.");

        // Check for task ID collision (rare but possible with rapid creation)
        if (Directory.Exists(fullTaskDir))
            throw AppServerErrors.TaskAlreadyExists(taskId);

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
            throw p.SourceName.Contains('/')
                ? AppServerErrors.SourceNotFound(p.SourceName)
                : AppServerErrors.TaskNotFound(p.TaskId, p.SourceName);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
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
            throw p.SourceName.Contains('/')
                ? AppServerErrors.SourceNotFound(p.SourceName)
                : AppServerErrors.TaskNotFound(p.TaskId, p.SourceName);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
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
            throw p.SourceName.Contains('/')
                ? AppServerErrors.SourceNotFound(p.SourceName)
                : AppServerErrors.TaskNotFound(p.TaskId, p.SourceName);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
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

    /// <summary>
    /// Validates that a path does not contain directory traversal sequences.
    /// </summary>
    private static bool ContainsDirectoryTraversal(string path)
    {
        // Check for parent directory traversal
        if (path.Contains("..", StringComparison.Ordinal))
            return true;

        // Check for null bytes (potential null byte injection)
        if (path.IndexOf('\0') >= 0)
            return true;

        return false;
    }

    /// <summary>
    /// Validates that a task ID is safe for use as a directory name.
    /// </summary>
    private static bool IsValidTaskId(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return false;

        // Must not contain directory traversal
        if (ContainsDirectoryTraversal(taskId))
            return false;

        // Must not contain path separators
        if (taskId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
            return false;

        // Must not be a reserved Windows name
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", 
            "LPT1", "LPT2", "LPT3", "LPT4" };
        if (reserved.Any(r => string.Equals(taskId, r, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if a directory already exists to prevent accidental overwrites.
    /// </summary>
    private bool TaskDirectoryExists(string taskId)
    {
        var taskDir = Path.Combine(fileStore.TasksRoot, taskId);
        return Directory.Exists(taskDir);
    }

    #endregion
}
