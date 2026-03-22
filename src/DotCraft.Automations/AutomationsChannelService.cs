using DotCraft.Abstractions;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Protocol;
using DotCraft.Security;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations;

/// <summary>
/// Channel service that runs the Automations orchestrator as a long-lived service.
/// </summary>
public sealed class AutomationsChannelService(
    AutomationOrchestrator orchestrator,
    DotCraftPaths paths,
    ILogger<AutomationsChannelService> logger)
    : ISessionServiceConsumer, IAutomationsChannelService
{
    private ISessionService? _sessionService;

    public string Name => "automations";

    public HeartbeatService? HeartbeatService { get; set; }

    public CronService? CronService { get; set; }

    public IApprovalService? ApprovalService => null;

    public object? ChannelClient => null;

    /// <inheritdoc />
    public void SetSessionService(ISessionService service) => _sessionService = service;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_sessionService == null)
            throw new InvalidOperationException(
                "Session service was not set. Automations requires GatewayHost to supply the shared ISessionService.");

        var client = new AutomationSessionClient(_sessionService, paths);
        orchestrator.SetSessionClient(client);
        await orchestrator.StartAsync(cancellationToken);

        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
    }

    public Task StopAsync() => orchestrator.StopAsync();

    public Task DeliverMessageAsync(string target, string content)
    {
        var preview = string.IsNullOrEmpty(content) ? "" : content[..Math.Min(content.Length, 200)];
        logger.LogInformation("[Automations -> {Target}] {Content}", target, preview);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
