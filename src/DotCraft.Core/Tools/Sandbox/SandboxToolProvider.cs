using DotCraft.Abstractions;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// Provides sandbox-isolated tools as an alternative to <see cref="CoreToolProvider"/>.
/// When sandbox mode is enabled, this provider supplies shell and file tools that
/// execute inside an OpenSandbox container instead of on the host machine.
/// Web tools and agent tools remain unchanged (they don't need isolation).
/// </summary>
public sealed class SandboxToolProvider : IAgentToolProvider
{
    /// <inheritdoc />
    public int Priority => 10; // Same priority as CoreToolProvider

    /// <inheritdoc />
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var sandboxConfig = context.Config.Tools.Sandbox;
        if (!sandboxConfig.Enabled)
            return [];

        var tools = new List<AITool>();
        var requireOutside =
            context.RequireApprovalOutsideWorkspace ?? context.Config.Tools.File.RequireApprovalOutsideWorkspace;

        // Create sandbox session manager and register for disposal
        var sandboxManager = new SandboxSessionManager(sandboxConfig, context.WorkspacePath);
        context.DisposableResources.Add(sandboxManager);

        // Override the default HostAgentFileSystem with sandbox-aware implementation
        // so channel tools can transparently access sandbox files.
        context.AgentFileSystem = new SandboxAgentFileSystem(sandboxManager);

        // Sandbox shell tools (replaces ShellTools)
        var shellTools = new SandboxShellTools(
            sandboxManager,
            context.Config.Tools.Shell.Timeout,
            context.Config.Tools.Shell.MaxOutputLength);
        tools.Add(AIFunctionFactory.Create(shellTools.Exec));

        // Sandbox file tools (replaces FileTools)
        var fileTools = new SandboxFileTools(
            sandboxManager,
            context.Config.Tools.File.MaxFileSize);
        tools.Add(AIFunctionFactory.Create(fileTools.ReadFile));
        tools.Add(AIFunctionFactory.Create(fileTools.WriteFile));
        tools.Add(AIFunctionFactory.Create(fileTools.EditFile));
        tools.Add(AIFunctionFactory.Create(fileTools.GrepFiles));
        tools.Add(AIFunctionFactory.Create(fileTools.FindFiles));

        // Agent tools (subagent spawning) — still needed, uses sandbox-aware manager
        var subAgentChatClient = context.OpenAIClientProvider.GetSubAgentChatClient(
            context.Config,
            context.EffectiveMainModel);
        var subAgentManager = new SubAgentManager(
            subAgentChatClient,
            context.WorkspacePath,
            context.Config.SubagentMaxToolCallRounds,
            maxConcurrency: context.Config.SubagentMaxConcurrency,
            shellTimeout: context.Config.Tools.Shell.Timeout,
            requireApprovalOutsideWorkspace: requireOutside,
            reasoningConfig: context.Config.Reasoning,
            blacklist: context.PathBlacklist,
            sandboxManager: sandboxManager,
            approvalService: context.ApprovalService,
            traceCollector: context.TraceCollector);
        var subAgentCoordinator = new SubAgentCoordinator(
            context.WorkspacePath,
            [new NativeSubAgentRuntime(subAgentManager), new CliOneshotRuntime()],
            context.Config.SubAgentProfiles,
            context.ApprovalService,
            context.Config.SubAgent.DisabledProfiles,
            context.ExternalCliSessionStore,
            context.Config.SubAgent.EnableExternalCliSessionResume);
        var agentTools = new AgentTools(subAgentCoordinator);
        tools.Add(AIFunctionFactory.Create(agentTools.SpawnSubagent));

        // Web tools — no isolation needed, reuse as-is
        var webTools = new WebTools(
            context.Config.Tools.Web.MaxChars,
            context.Config.Tools.Web.Timeout,
            context.Config.Tools.Web.SearchMaxResults,
            context.Config.Tools.Web.SearchProvider);
        tools.Add(AIFunctionFactory.Create(webTools.WebSearch));
        tools.Add(AIFunctionFactory.Create(webTools.WebFetch));

        return tools;
    }
}
