using DotCraft.Abstractions;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Security;
using DotCraft.GitHubTracker.Orchestrator;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker;

/// <summary>
/// Channel service that runs the GitHubTracker orchestrator as a long-lived service.
/// Works both standalone and as a Gateway sub-channel.
/// </summary>
public sealed class GitHubTrackerChannelService(
    GitHubTrackerOrchestrator orchestrator,
    ILogger<GitHubTrackerChannelService> logger)
    : IChannelService
{
    public string Name => "github-tracker";
    public HeartbeatService? HeartbeatService { get; set; }
    public CronService? CronService { get; set; }
    public IApprovalService? ApprovalService => null;
    public object? ChannelClient => null;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await orchestrator.StartAsync(cancellationToken);

        // Wait until canceled
        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
    }

    public async Task StopAsync()
    {
        await orchestrator.StopAsync();
    }

    public Task DeliverMessageAsync(string target, string content)
    {
        logger.LogInformation("[GitHubTracker -> {Target}] {Content}", target, content[..Math.Min(content.Length, 200)]);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await orchestrator.DisposeAsync();
    }
}
