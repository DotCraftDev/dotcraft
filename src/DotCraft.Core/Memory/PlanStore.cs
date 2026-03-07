using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotCraft.Memory;

/// <summary>
/// Persists plan files to disk. Supports both structured JSON plans and
/// legacy raw-markdown plans for backward compatibility.
/// </summary>
public sealed class PlanStore(string botPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private string PlansDir => Path.Combine(botPath, "plans");

    // ── Structured plan (JSON + rendered MD) ──

    public string GetStructuredPlanPath(string sessionId)
        => Path.Combine(PlansDir, $"{sessionId}.json");

    public async Task SaveStructuredPlanAsync(string sessionId, StructuredPlan plan)
    {
        Directory.CreateDirectory(PlansDir);

        var json = JsonSerializer.Serialize(plan, JsonOptions);
        await File.WriteAllTextAsync(GetStructuredPlanPath(sessionId), json, Encoding.UTF8);

        // Also render a human-readable .md alongside
        var md = RenderPlanMarkdown(plan);
        await File.WriteAllTextAsync(GetPlanPath(sessionId), md, Encoding.UTF8);
    }

    public async Task<StructuredPlan?> LoadStructuredPlanAsync(string sessionId)
    {
        var path = GetStructuredPlanPath(sessionId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<StructuredPlan>(json, ReadOptions);
        }
        catch
        {
            return null;
        }
    }

    public bool StructuredPlanExists(string sessionId)
        => File.Exists(GetStructuredPlanPath(sessionId));

    /// <summary>
    /// Renders a <see cref="StructuredPlan"/> as human-readable Markdown.
    /// </summary>
    public static string RenderPlanMarkdown(StructuredPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {plan.Title}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(plan.Overview))
        {
            sb.AppendLine($"> {plan.Overview}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(plan.Content))
        {
            sb.AppendLine(plan.Content);
            sb.AppendLine();
        }

        if (plan.Todos.Count > 0)
        {
            sb.AppendLine("## Tasks");
            sb.AppendLine();
            foreach (var todo in plan.Todos)
            {
                var checkbox = todo.Status is PlanTodoStatus.Completed ? "[x]" : "[ ]";
                var statusTag = todo.Status switch
                {
                    PlanTodoStatus.InProgress => " *(in progress)*",
                    PlanTodoStatus.Cancelled => " *(cancelled)*",
                    _ => ""
                };
                sb.AppendLine($"- {checkbox} `{todo.Id}` — {todo.Content}{statusTag}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    // ── Legacy raw-markdown plan ──

    public string GetPlanPath(string sessionId)
        => Path.Combine(PlansDir, $"{sessionId}.md");

    public async Task SavePlanAsync(string sessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        Directory.CreateDirectory(PlansDir);
        await File.WriteAllTextAsync(GetPlanPath(sessionId), content, Encoding.UTF8);
    }

    public async Task<string?> LoadPlanAsync(string sessionId)
    {
        var path = GetPlanPath(sessionId);
        if (!File.Exists(path))
            return null;

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    public bool PlanExists(string sessionId)
        => StructuredPlanExists(sessionId) || File.Exists(GetPlanPath(sessionId));

    public void DeletePlan(string sessionId)
    {
        var jsonPath = GetStructuredPlanPath(sessionId);
        if (File.Exists(jsonPath))
            File.Delete(jsonPath);

        var mdPath = GetPlanPath(sessionId);
        if (File.Exists(mdPath))
            File.Delete(mdPath);
    }
}
