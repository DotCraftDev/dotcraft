using System.Collections.Concurrent;
using System.Reflection;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Regression: <see cref="SessionService.SetThreadModeAsync"/> must rebuild the per-thread agent via
/// <see cref="SessionService"/> (same path as config updates) so <see cref="ThreadConfiguration.Model"/> is not lost.
/// </summary>
public sealed class SessionServiceSetThreadModeTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceSetThreadModeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SetThreadMode_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task SetThreadModeAsync_KeepsPerThreadChatClient_WhenModelOverrideIsSet()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };

        const string modelOverride = "gpt-per-thread-model-override";

        await using var agentFactory = CreateAgentFactory();
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var svc = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());

        var config = new ThreadConfiguration
        {
            Mode = "agent",
            Model = modelOverride
        };
        var thread = await svc.CreateThreadAsync(identity, config);

        await svc.SetThreadModeAsync(thread.Id, "plan");

        var innerAfterModeSwitch = GetInnermostChatClient(GetCachedThreadAgent(svc, thread.Id));
        Assert.NotNull(innerAfterModeSwitch);

        var loaded = await store.LoadThreadAsync(thread.Id);
        Assert.Equal("plan", loaded?.Configuration?.Mode);
        Assert.Equal(modelOverride, loaded?.Configuration?.Model);

        // Old bug: CreateAgentForMode ignored thread.Model — inner client matched the global default agent.
        var defaultPlanAgent = agentFactory.CreateAgentForMode(AgentMode.Plan);
        var innerDefaultPlan = GetInnermostChatClient(defaultPlanAgent);
        Assert.NotSame(innerDefaultPlan, innerAfterModeSwitch);
    }

    private AgentFactory CreateAgentFactory()
    {
        var config = new AppConfig
        {
            ApiKey = "sk-test-not-used-for-network",
            EndPoint = "https://127.0.0.1:9/v1"
        };
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: Array.Empty<IAgentToolProvider>());
    }

    private static AIAgent GetCachedThreadAgent(SessionService svc, string threadId)
    {
        var field = typeof(SessionService).GetField("_threadAgents", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var dict = (ConcurrentDictionary<string, AIAgent>)field.GetValue(svc)!;
        Assert.True(dict.TryGetValue(threadId, out var agent));
        return agent;
    }

    private static IChatClient? GetInnermostChatClient(AIAgent agent)
    {
        var client = TryGetChatClientFromAgent(agent);
        return UnwrapToInnermost(client);
    }

    private static IChatClient? TryGetChatClientFromAgent(AIAgent agent)
    {
        for (var t = agent.GetType(); t != null; t = t.BaseType)
        {
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(IChatClient).IsAssignableFrom(p.PropertyType))
                    continue;
                if (p.GetIndexParameters().Length != 0)
                    continue;
                if (p.GetValue(agent) is IChatClient chat)
                    return chat;
            }
        }

        return null;
    }

    private static IChatClient? UnwrapToInnermost(IChatClient? client)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        while (client != null && seen.Add(client))
        {
            IChatClient? next = null;
            var t = client.GetType();
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.PropertyType != typeof(IChatClient) && !typeof(IChatClient).IsAssignableFrom(p.PropertyType))
                    continue;
                if (p.GetIndexParameters().Length != 0)
                    continue;
                if (p.GetValue(client) is IChatClient inner)
                {
                    next = inner;
                    break;
                }
            }

            if (next == null)
                return client;
            client = next;
        }

        return client;
    }
}
