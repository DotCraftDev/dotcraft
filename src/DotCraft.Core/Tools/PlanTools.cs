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

    [Description("""
        Create or update a structured task list for the current session. This helps track progress and organize complex multi-step work.

        When to use this tool:
        - Complex multi-step tasks (3+ distinct steps)
        - Non-trivial tasks requiring planning or multiple operations
        - User provides multiple tasks (numbered or comma-separated)
        - After receiving new instructions, capture requirements as todos
        - When starting a task, mark it as in_progress; when done, mark it as completed

        When NOT to use this tool:
        - Single, straightforward tasks
        - Trivial tasks completable in fewer than 3 steps
        - Purely conversational or informational requests
        - Do NOT include operational steps like linting, testing, or searching the codebase as todo items

        Parameter 'merge':
        - false (default): Replace the entire todo list with the provided items
        - true: Merge updates into the existing list by id. Matched items are updated; new items are added; unmentioned items are left unchanged

        Task states: pending, in_progress, completed, cancelled

        Rules:
        - Create the full todo list BEFORE starting work
        - Mark tasks completed IMMEDIATELY after finishing (do not batch completions)
        - Only ONE task should be in_progress at a time
        - Keep items high-level and actionable
        """)]
    [Tool(Icon = "📝", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.TodoWrite))]
    public async Task<string> TodoWrite(
        [Description("Array of todo items. Each item has 'id' (short kebab-case), 'content' (task description), and 'status' (pending | in_progress | completed | cancelled).")]
        List<TodoWriteInput> todos,
        [Description("When false (default), replace the entire todo list. When true, merge updates into the existing list by id.")]
        bool merge = false)
    {
        try
        {
            var sessionId = sessionIdProvider();
            if (string.IsNullOrEmpty(sessionId))
            {
                DebugModeService.LogIfEnabled("[PlanTools] TodoWrite: sessionId is null or empty");
                return "Error: No active session.";
            }

            var validItems = todos
                .Where(t => !string.IsNullOrWhiteSpace(t.Id) && !string.IsNullOrWhiteSpace(t.Content))
                .ToList();

            if (validItems.Count == 0)
            {
                DebugModeService.LogIfEnabled("[PlanTools] TodoWrite: No valid items provided");
                return "Error: No valid todo items provided.";
            }

            var now = DateTimeOffset.UtcNow;
            var existing = await planStore.LoadStructuredPlanAsync(sessionId);

            StructuredPlan plan;

            if (merge && existing != null)
            {
                // Merge: update matched items by id, append new ones
                plan = existing;
                foreach (var item in validItems)
                {
                    var normalizedStatus = NormalizeStatus(item.Status);
                    var existingTodo = plan.Todos.FirstOrDefault(t => t.Id == item.Id.Trim());
                    if (existingTodo != null)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Content))
                            existingTodo.Content = item.Content.Trim();
                        if (!string.IsNullOrWhiteSpace(normalizedStatus))
                            existingTodo.Status = normalizedStatus;
                    }
                    else
                    {
                        plan.Todos.Add(new PlanTodo
                        {
                            Id = item.Id.Trim(),
                            Content = item.Content.Trim(),
                            Priority = PlanTodoPriority.Medium,
                            Status = normalizedStatus is { Length: > 0 } s ? s : PlanTodoStatus.Pending
                        });
                    }
                }
                plan.UpdatedAt = now;
            }
            else
            {
                // Replace: build a fresh todo list; create the plan if it doesn't exist yet
                var todoList = validItems
                    .Select(t => new PlanTodo
                    {
                        Id = t.Id.Trim(),
                        Content = t.Content.Trim(),
                        Priority = PlanTodoPriority.Medium,
                        Status = NormalizeStatus(t.Status) is { Length: > 0 } s ? s : PlanTodoStatus.Pending
                    })
                    .ToList();

                plan = new StructuredPlan
                {
                    Title = existing?.Title ?? "Task Tracking",
                    Overview = existing?.Overview ?? "",
                    Content = existing?.Content ?? "",
                    Todos = todoList,
                    CreatedAt = existing?.CreatedAt ?? now,
                    UpdatedAt = now
                };
            }

            await planStore.SaveStructuredPlanAsync(sessionId, plan);
            onPlanUpdated?.Invoke(plan);

            var action = merge && existing != null ? "Updated" : "Created";
            DebugModeService.LogIfEnabled($"[PlanTools] TodoWrite: {action} {plan.Todos.Count} task(s) for session {sessionId}");
            return $"{action} task list with {plan.Todos.Count} item(s).";
        }
        catch (Exception ex)
        {
            DebugModeService.LogIfEnabled($"[PlanTools] TodoWrite exception: {ex.Message}\n{ex.StackTrace}");
            return $"Error: Failed to write todos - {ex.Message}";
        }
    }

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "";
        var s = status.Trim().ToLowerInvariant();
        return s is PlanTodoStatus.Pending or PlanTodoStatus.InProgress
            or PlanTodoStatus.Completed or PlanTodoStatus.Cancelled
            ? s : "";
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

/// <summary>
/// Input DTO for a single todo item in TodoWrite.
/// </summary>
public sealed class TodoWriteInput
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public string Status { get; set; } = PlanTodoStatus.Pending;
}
