using DotCraft.Abstractions;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Provides core tools: file operations, shell execution, web tools, and agent spawning.
/// These tools are available in all running modes.
/// </summary>
public sealed class CoreToolProvider : IAgentToolProvider
{
    /// <inheritdoc />
    public int Priority => 10; // Core tools have highest priority (lowest number)

    /// <inheritdoc />
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        // When sandbox mode is enabled, SandboxToolProvider supplies shell/file/agent tools.
        // CoreToolProvider only provides web tools in that case to avoid duplication.
        if (context.Config.Tools.Sandbox.Enabled)
            return [];

        var tools = new List<AITool>();
        var requireOutside =
            context.RequireApprovalOutsideWorkspace ?? context.Config.Tools.File.RequireApprovalOutsideWorkspace;

        // Agent tools (subagent spawning)
        var subAgentManager = new SubAgentManager(
            context.ChatClient,
            context.WorkspacePath,
            context.Config.SubagentMaxToolCallRounds,
            maxConcurrency: context.Config.SubagentMaxConcurrency,
            shellTimeout: context.Config.Tools.Shell.Timeout,
            requireApprovalOutsideWorkspace: requireOutside,
            reasoningConfig: context.Config.Reasoning,
            blacklist: context.PathBlacklist,
            approvalService: context.ApprovalService,
            traceCollector: context.TraceCollector);
        var subAgentCoordinator = new SubAgentCoordinator(
            context.WorkspacePath,
            [new NativeSubAgentRuntime(subAgentManager), new CliOneshotRuntime()],
            context.Config.SubAgentProfiles,
            context.ApprovalService);
        var agentTools = new AgentTools(subAgentCoordinator);
        tools.Add(AIFunctionFactory.Create(agentTools.SpawnSubagent));

        // File tools
        var userDotCraftPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft"));
        var fileTools = new FileTools(
            context.WorkspacePath,
            requireOutside,
            context.Config.Tools.File.MaxFileSize,
            context.ApprovalService,
            context.PathBlacklist,
            trustedReadPaths: [userDotCraftPath],
            lspServerManager: context.LspServerManager);
        tools.Add(AIFunctionFactory.Create(fileTools.ReadFile));
        tools.Add(AIFunctionFactory.Create(fileTools.WriteFile));
        tools.Add(AIFunctionFactory.Create(fileTools.EditFile));
        tools.Add(AIFunctionFactory.Create(fileTools.GrepFiles));
        tools.Add(AIFunctionFactory.Create(fileTools.FindFiles));

        // LSP tool
        if (context.Config.Tools.Lsp.Enabled && context.LspServerManager != null)
        {
            var lspTool = new LspTool(
                context.WorkspacePath,
                context.LspServerManager,
                requireOutside,
                context.Config.Tools.Lsp.MaxFileSize,
                context.ApprovalService,
                context.PathBlacklist);
            tools.Add(AIFunctionFactory.Create(lspTool.LSP));
        }

        // Shell tools
        var shellTools = new ShellTools(
            context.WorkspacePath,
            context.Config.Tools.Shell.Timeout,
            requireOutside,
            context.Config.Tools.Shell.MaxOutputLength,
            approvalService: context.ApprovalService,
            blacklist: context.PathBlacklist);
        tools.Add(AIFunctionFactory.Create(shellTools.Exec));

        // Web tools
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
