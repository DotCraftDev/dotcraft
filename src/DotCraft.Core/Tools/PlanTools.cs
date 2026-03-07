using System.ComponentModel;
using System.Text.Json;
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
        [Description("A JSON array of task items, each with 'id' (short kebab-case identifier), 'content' (description), and optional 'priority' (high/medium/low, default medium). Example: [{\"id\":\"add-auth\",\"content\":\"Add authentication middleware\",\"priority\":\"high\"}]")] string? todos = null)
    {
        try
        {
            var sessionId = sessionIdProvider();
            if (string.IsNullOrEmpty(sessionId))
            {
                DebugModeService.LogIfEnabled("[PlanTools] CreatePlan: sessionId is null or empty");
                return "Error: No active session.";
            }

            var todoList = ParseTodos(todos);

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
        [Description("A JSON array of status updates. Each item has 'id' (task id) and 'status' (pending | in_progress | completed | cancelled). Example: [{\"id\":\"add-auth\",\"status\":\"completed\"}]")]
        string updates)
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

            List<TodoStatusUpdate>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<List<TodoStatusUpdate>>(updates, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                DebugModeService.LogIfEnabled($"[PlanTools] UpdateTodos: JSON parse error - {ex.Message}");
                return "Error: Invalid JSON. Expected [{\"id\":\"...\",\"status\":\"...\"}].";
            }

            if (parsed == null || parsed.Count == 0)
            {
                DebugModeService.LogIfEnabled("[PlanTools] UpdateTodos: No updates provided");
                return "Error: No updates provided.";
            }

            var results = new List<string>();
            foreach (var upd in parsed)
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

    private sealed class TodoStatusUpdate
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
    }

    private static List<PlanTodo> ParseTodos(string? todosJson)
    {
        if (string.IsNullOrWhiteSpace(todosJson))
            return [];

        try
        {
            var items = JsonSerializer.Deserialize<List<TodoInput>>(todosJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items == null)
                return [];

            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Content))
                .Select(i => new PlanTodo
                {
                    Id = i.Id!.Trim(),
                    Content = i.Content!.Trim(),
                    Priority = NormalizePriority(i.Priority),
                    Status = PlanTodoStatus.Pending
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizePriority(string? priority)
    {
        return priority?.Trim().ToLowerInvariant() switch
        {
            PlanTodoPriority.High => PlanTodoPriority.High,
            PlanTodoPriority.Low => PlanTodoPriority.Low,
            _ => PlanTodoPriority.Medium
        };
    }

    private sealed class TodoInput
    {
        public string? Id { get; set; }
        public string? Content { get; set; }
        public string? Priority { get; set; }
    }
}
