using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Tracing;
using DotCraft.Memory;
using DotCraft.Skills;
using Microsoft.Agents.AI;

namespace DotCraft.Context;

/// <summary>
/// Enhanced context provider combining memory, skills, and system prompt.
/// </summary>
public sealed class MemoryContextProvider(
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    string dotCraftPath,
    string workspacePath,
    TraceCollector? traceCollector = null,
    Func<IReadOnlyList<string>>? toolNamesProvider = null,
    CustomCommandLoader? customCommandLoader = null,
    AgentModeManager? modeManager = null,
    PlanStore? planStore = null,
    Func<string?>? sessionIdProvider = null,
    bool sandboxEnabled = false,
    IReadOnlyList<string>? deferredMcpServerNames = null)
    : AIContextProvider
{
    private readonly PromptBuilder _promptBuilder = new(memoryStore, skillsLoader, dotCraftPath, workspacePath, customCommandLoader, modeManager, planStore, sessionIdProvider, sandboxEnabled, deferredMcpServerNames);

    protected override ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var systemPrompt = _promptBuilder.BuildSystemPrompt();
        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (!string.IsNullOrWhiteSpace(sessionKey))
            traceCollector?.RecordSessionMetadata(sessionKey, systemPrompt, toolNamesProvider?.Invoke());

        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = systemPrompt
        });
    }

    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return JsonSerializer.SerializeToElement(new ContextSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow
        }, jsonSerializerOptions);
    }

    private sealed class ContextSnapshot
    {
        public DateTimeOffset Timestamp { get; set; }
    }
}
