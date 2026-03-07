using System.ComponentModel;
using DotCraft.Diagnostics;
using DotCraft.Memory;

namespace DotCraft.Tools;

/// <summary>
/// Tools for creating and managing structured plans in Plan mode.
/// </summary>
public sealed class PlanTools(
    PlanStore planStore,
    Func<string?> sessionIdProvider,
    Action<StructuredPlan>? onPlanUpdated = null)
{
    [Description("Create or replace the structured plan for the current session. Call this tool to present your finalized plan to the user. The plan should include a title, a brief overview, detailed implementation content in Markdown, and a list of actionable task items.")]
    [Tool(Icon = "📋", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.CreatePlan))]
    public async Task<string> CreatePlan(
        [Description("A concise title for the plan.")] string title,
        [Description("A 1-2 sentence summary of what the plan accomplishes.")] string overview,
        [Description("The detailed plan content in Markdown. Include specific file paths, implementation details, and verification steps.")] string plan,
        [Description("Actionable task items. Each item has 'id' (short kebab-case) and 'content' (task description).")] List<PlanTodoInput> todos)
    {
        try
        {
            var sessionId = sessionIdProvider();
            if (string.IsNullOrEmpty(sessionId))
            {
                DebugModeService.LogIfEnabled("[PlanTools] CreatePlan: sessionId is null or empty");
                return "Error: No active session.";
            }

            var todoList = todos
                .Where(t => !string.IsNullOrWhiteSpace(t.Id) && !string.IsNullOrWhiteSpace(t.Content))
                .Select(t => new PlanTodo
                {
                    Id = t.Id.Trim(),
                    Content = t.Content.Trim(),
                    Priority = PlanTodoPriority.Medium,
                    Status = PlanTodoStatus.Pending
                })
                .ToList();

            var now = DateTimeOffset.UtcNow;
            var existing = await planStore.LoadStructuredPlanAsync(sessionId);

            var structured = new StructuredPlan
            {
                Title = title,
                Overview = overview,
                Content = plan,
                Todos = todoList,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            };

            await planStore.SaveStructuredPlanAsync(sessionId, structured);
            onPlanUpdated?.Invoke(structured);

            var taskSummary = todoList.Count > 0
                ? $" with {todoList.Count} task(s)"
                : "";
            return $"Plan \"{title}\" saved successfully{taskSummary}. Switch to agent mode to execute.";
        }
        catch (Exception ex)
        {
            DebugModeService.LogIfEnabled($"[PlanTools] CreatePlan exception: {ex.Message}");
            return $"Error: Failed to create plan - {ex.Message}";
        }
    }

    [Description("Update the status of one or more tasks in the current plan. Call this to mark tasks as in_progress when you start working on them and completed when done.")]
    [Tool(Icon = "✅", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.UpdateTodos))]
    public async Task<string> UpdateTodos(
        [Description("Status updates. Each item has 'id' (task id) and 'status' (pending | in_progress | completed | cancelled).")]
        List<TodoStatusUpdateInput> updates)
    {
        try
        {
            var sessionId = sessionIdProvider();
            if (string.IsNullOrEmpty(sessionId))
            {
                DebugModeService.LogIfEnabled("[PlanTools] UpdateTodos: sessionId is null or empty");
                return "Error: No active session. Please ensure you are in agent mode with an active session.";
            }

            var plan = await planStore.LoadStructuredPlanAsync(sessionId);
            if (plan == null)
            {
                DebugModeService.LogIfEnabled($"[PlanTools] UpdateTodos: No plan exists for session {sessionId}");
                return "Error: No plan exists for the current session. Create a plan first using CreatePlan in plan mode.";
            }

            if (updates.Count == 0)
            {
                DebugModeService.LogIfEnabled("[PlanTools] UpdateTodos: No updates provided");
                return "Error: No updates provided.";
            }

            var results = new List<string>();
            foreach (var upd in updates)
            {
                if (string.IsNullOrWhiteSpace(upd.Id) || string.IsNullOrWhiteSpace(upd.Status))
                    continue;

                var todo = plan.Todos.FirstOrDefault(t => t.Id == upd.Id.Trim());
                if (todo == null)
                {
                    results.Add($"{upd.Id} -> not found");
                    continue;
                }

                var normalizedStatus = upd.Status.Trim().ToLowerInvariant();
                if (normalizedStatus is not (PlanTodoStatus.Pending or PlanTodoStatus.InProgress
                    or PlanTodoStatus.Completed or PlanTodoStatus.Cancelled))
                {
                    results.Add($"{upd.Id} -> invalid status '{upd.Status}'");
                    continue;
                }

                todo.Status = normalizedStatus;
                results.Add($"{upd.Id} -> {normalizedStatus}");
            }

            if (results.Count == 0)
            {
                DebugModeService.LogIfEnabled("[PlanTools] UpdateTodos: No tasks were updated");
                return "No tasks were updated.";
            }

            plan.UpdatedAt = DateTimeOffset.UtcNow;
            await planStore.SaveStructuredPlanAsync(sessionId, plan);
            onPlanUpdated?.Invoke(plan);
            DebugModeService.LogIfEnabled($"[PlanTools] UpdateTodos: Updated {results.Count} task(s) for session {sessionId}");
            return $"Updated {results.Count} task(s): {string.Join(", ", results)}";
        }
        catch (Exception ex)
        {
            DebugModeService.LogIfEnabled($"[PlanTools] UpdateTodos exception: {ex.Message}\n{ex.StackTrace}");
            return $"Error: Failed to update todos - {ex.Message}";
        }
    }
}

/// <summary>
/// Input DTO for a single todo item in CreatePlan.
/// </summary>
public sealed class PlanTodoInput
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// Input DTO for a single todo status update in UpdateTodos.
/// </summary>
public sealed class TodoStatusUpdateInput
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
}
