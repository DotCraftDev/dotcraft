using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Tools.Sandbox;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace DotCraft.Agents;

/// <summary>
/// Manages subagent execution using the AIFunction pattern.
/// Subagents are lightweight agent instances that handle specific tasks and return results directly to the main agent.
/// </summary>
/// <remarks>
/// Implementation uses AIAgent.AsAIFunction() for native framework support.
/// Subagents have restricted tool access for security.
/// A <see cref="SemaphoreSlim"/> throttles concurrent subagent executions to avoid exceeding API rate limits.
/// </remarks>
public sealed class SubAgentManager
{
    private readonly ChatClient _chatClient;

    private readonly string _workspaceRoot;

    private readonly int _maxToolCallRounds;

    private readonly SemaphoreSlim _concurrencyGate;

    private readonly FileTools? _fileTools;

    private readonly ShellTools? _shellTools;

    private readonly SandboxShellTools? _sandboxShellTools;

    private readonly SandboxFileTools? _sandboxFileTools;

    private readonly WebTools _webTools;

    private readonly bool _useSandbox;

    private readonly AppConfig.ReasoningConfig _reasoningConfig;

    public SubAgentManager(
        ChatClient chatClient, 
        string workspaceRoot, 
        int maxToolCallRounds = 15,
        int maxConcurrency = 3,
        int shellTimeout = 60,
        AppConfig.ReasoningConfig? reasoningConfig = null,
        PathBlacklist? blacklist = null,
        SandboxSessionManager? sandboxManager = null)
    {
        _chatClient = chatClient;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _maxToolCallRounds = maxToolCallRounds;
        _concurrencyGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _useSandbox = sandboxManager != null;
        _reasoningConfig = reasoningConfig ?? new AppConfig.ReasoningConfig();

        if (sandboxManager != null)
        {
            // Sandbox mode: subagents execute inside containers
            _sandboxShellTools = new SandboxShellTools(sandboxManager, shellTimeout);
            _sandboxFileTools = new SandboxFileTools(sandboxManager);
        }
        else
        {
            // Local mode: existing behavior
            _fileTools = new FileTools(
                workspaceRoot: _workspaceRoot,
                requireApprovalOutsideWorkspace: false,
                maxFileSize: 100000,
                approvalService: null,
                blacklist: blacklist
            );
            
            _shellTools = new ShellTools(
                workingDirectory: _workspaceRoot,
                timeoutSeconds: shellTimeout,
                requireApprovalOutsideWorkspace: false,
                maxOutputLength: 10000,
                approvalService: null,
                blacklist: blacklist
            );
        }
        
        _webTools = new WebTools(
            maxChars: 50000,  // Limit web content size for subagents
            timeoutSeconds: 30
        );
    }

    /// <summary>
    /// Creates an AIFunction that wraps a subagent for the given task.
    /// This allows the main agent to invoke the subagent as a tool and receive results directly.
    /// </summary>
    public AIFunction CreateSubAgentFunction(string taskDescription)
    {
        // Create the subagent with restricted tools
        var subagent = CreateSubAgent(taskDescription);
        
        // Wrap as AIFunction - the framework handles execution and result passing
        return subagent.AsAIFunction(
            options: new AIFunctionFactoryOptions
            {
                Name = "execute_subagent_task",
                Description = $"Execute a subagent to handle the following task: {taskDescription}"
            }
        );
    }

    /// <summary>
    /// Spawn a subagent to execute a task. Returns an AIFunction that the main agent can call.
    /// </summary>
    public async Task<string> SpawnAsync(string task, string? label = null)
    {
        try
        {
            // Throttle concurrent subagent executions to respect API rate limits
            await _concurrencyGate.WaitAsync();
            try
            {
                var subagent = CreateSubAgent(task);
                var result = await subagent.RunAsync(task);
                return result.Text;
            }
            finally
            {
                _concurrencyGate.Release();
            }
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Create a subagent with restricted tools for a specific task.
    /// </summary>
    private ChatClientAgent CreateSubAgent(string task)
    {
        var systemPrompt = BuildSubAgentPrompt(task);

        // Reuse existing tool classes with restricted configuration
        // Tool restrictions (workspace-only, no approval) are enforced via constructor parameters
        var tools = new List<AITool>();

        if (_useSandbox && _sandboxFileTools != null && _sandboxShellTools != null)
        {
            // Sandbox mode: use isolated container tools
            tools.Add(AIFunctionFactory.Create(_sandboxFileTools.ReadFile));
            tools.Add(AIFunctionFactory.Create(_sandboxFileTools.WriteFile));
            tools.Add(AIFunctionFactory.Create(_sandboxFileTools.GrepFiles));
            tools.Add(AIFunctionFactory.Create(_sandboxFileTools.FindFiles));
            tools.Add(AIFunctionFactory.Create(_sandboxShellTools.Exec));
        }
        else if (_fileTools != null && _shellTools != null)
        {
            // Local mode: existing behavior
            tools.Add(AIFunctionFactory.Create(_fileTools.ReadFile));
            tools.Add(AIFunctionFactory.Create(_fileTools.WriteFile));
            tools.Add(AIFunctionFactory.Create(_fileTools.GrepFiles));
            tools.Add(AIFunctionFactory.Create(_fileTools.FindFiles));
            tools.Add(AIFunctionFactory.Create(_shellTools.Exec));
        }

        tools.Add(AIFunctionFactory.Create(_webTools.WebSearch));
        tools.Add(AIFunctionFactory.Create(_webTools.WebFetch));

        // Create a chat client pipeline with custom FunctionInvokingChatClient configuration for subagent
        var chatClientBuilder = new ChatClientBuilder(_chatClient.AsIChatClient());
        chatClientBuilder.Use(innerClient => new FunctionInvokingChatClient(innerClient)
        {
            MaximumIterationsPerRequest = _maxToolCallRounds,
            AllowConcurrentInvocation = true
        });
        var configuredChatClient = chatClientBuilder.Build();

        var options = new ChatClientAgentOptions
        {
            Name = "SubAgent",
            UseProvidedChatClientAsIs = true,  // Use our custom-configured chat client as-is
            ChatOptions = new ChatOptions
            {
                Instructions = systemPrompt,
                Tools = tools,
                Reasoning = _reasoningConfig.ToOptions()
            }
        };

        return configuredChatClient.AsAIAgent(options);
    }

    private string BuildSubAgentPrompt(string task)
    {
        return 
$"""
# Subagent

You are a subagent spawned by the main agent to complete a specific task.

## Your Task
{task}

## Rules
1. Stay focused - complete only the assigned task, nothing else
2. Your final response will be reported back to the main agent
3. Do not initiate conversations or take on side tasks
4. Be concise but informative in your findings

## What You Can Do
- Read files and list directory contents in the workspace
- Write files in the workspace
- Search file contents with regex (GrepFiles)
- Find files by name pattern (FindFiles)
- Execute shell commands
- Search the web
- Fetch web content
- Use these tools to complete your task thoroughly

## What You Cannot Do
- Delete files or directories (security restriction)
- Run commands outside the workspace (security restriction)
- Access system files or directories (security restriction)

## Workspace
Your workspace is at: {_workspaceRoot}

When you have completed the task, provide a clear summary of your findings or actions.
""";
    }
}
