using System.ComponentModel;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Protocol;

namespace DotCraft.Tools;

/// <summary>
/// Core tools for DotCraft agent.
/// </summary>
public sealed class AgentTools(SubAgentCoordinator? subAgentManager = null)
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerOptions.Web);

    [Description("""
        Spawn a session-backed subagent as a child thread.
        Use this for collaborative background work when the parent agent can continue while the child thread runs.
        Returns a compact JSON string. The returned childThreadId can be passed to SendInput, WaitAgent, ResumeAgent, and CloseAgent.
        Available profile names are listed in the system prompt. The default profile is native.
        External CLI profiles provide a persisted synthetic child turn with stage-level progress and a final result.
        SendInput works for native profiles and for external CLI profiles only when the profile supports resume and workspace resume is enabled.
        Child agent output is trusted for broad findings; the main agent owns synthesis and should inspect critical files when needed before finalizing a plan.
    """)]
    [Tool(Icon = "🐧", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.SpawnAgent))]
    [StreamArguments(false)]
    public async Task<string> SpawnAgent(
        [Description("Required non-empty self-contained task prompt for the child agent thread. Always provide this argument.")] string prompt,
        [Description("Optional short name shown in UI for this child agent.")] string? agentNickname = null,
        [Description("Optional role label such as worker, explorer, or reviewer.")] string? agentRole = null,
        [Description("Optional named subagent profile. Defaults to native when omitted. Use only profile names listed in the system prompt.")] string? profile = null,
        [Description("Optional working directory for the child thread. Defaults to the parent thread workspace.")] string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("SpawnAgent is available only inside a Session Core turn.");

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            sessionContext,
            new SubAgentSpawnOptions
            {
                Prompt = prompt,
                AgentNickname = agentNickname,
                AgentRole = agentRole,
                ProfileName = profile,
                WorkingDirectory = workingDirectory
            },
            waitForCompletion: false,
            subAgentManager,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Send another user message to a session-backed child agent thread.")]
    [Tool(Icon = "💬")]
    [StreamArguments(false)]
    public async Task<string> SendInput(
        [Description("Child agent thread id returned by SpawnAgent.")] string childThreadId,
        [Description("Message to send to the child agent.")] string message,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("SendInput is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.SendInputAsync(
            sessionContext.SessionService,
            childThreadId,
            message,
            subAgentManager,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Wait for a session-backed child agent thread to finish its current turn and return its final message.")]
    [Tool(Icon = "⏱️")]
    [StreamArguments(false)]
    public async Task<string> WaitAgent(
        [Description("Child agent thread id returned by SpawnAgent.")] string childThreadId,
        [Description("Optional timeout in seconds. Omit or pass 0 to wait without a timeout.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("WaitAgent is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.WaitAgentAsync(
            sessionContext.SessionService,
            childThreadId,
            timeoutSeconds,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Resume a paused or closed child agent thread and reopen its parent-child edge.")]
    [Tool(Icon = "▶️")]
    [StreamArguments(false)]
    public async Task<string> ResumeAgent(
        [Description("Child agent thread id returned by SpawnAgent.")] string childThreadId,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("ResumeAgent is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.ResumeAgentAsync(
            sessionContext.SessionService,
            childThreadId,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Close a child agent thread edge and cancel its active turn if one is running.")]
    [Tool(Icon = "⏹️")]
    [StreamArguments(false)]
    public async Task<string> CloseAgent(
        [Description("Child agent thread id returned by SpawnAgent.")] string childThreadId,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("CloseAgent is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.CloseAgentAsync(
            sessionContext.SessionService,
            childThreadId,
            cancellationToken);
        return SerializeResult(result);
    }

    private static string SerializeResult(SubAgentControlResult result) =>
        JsonSerializer.Serialize(result, ResultJsonOptions);
}
