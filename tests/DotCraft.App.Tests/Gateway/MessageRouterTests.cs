using DotCraft.Abstractions;
using DotCraft.Cron;
using DotCraft.Gateway;
using DotCraft.Heartbeat;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using Spectre.Console;

namespace DotCraft.Tests.Gateway;

public sealed class MessageRouterTests
{
    [Fact]
    public async Task BroadcastToAdminsAsync_LogsWhenDeliveryReturnsFalse()
    {
        var previousConsole = AnsiConsole.Console;
        using var writer = new StringWriter();
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No
        });

        try
        {
            var router = new MessageRouter(new ChannelRuntimeRegistry());
            router.RegisterChannel(new StubChannel(
                "qq",
                ["admin-user"],
                new ExtChannelSendResult
                {
                    Delivered = false,
                    ErrorCode = "AdapterDeliveryFailed",
                    ErrorMessage = "proactive delivery disabled"
                }));

            await router.BroadcastToAdminsAsync("heartbeat");

            var output = writer.ToString();
            Assert.Contains("qq admin notify to admin-user failed", output);
            Assert.Contains("AdapterDeliveryFailed", output);
        }
        finally
        {
            AnsiConsole.Console = previousConsole;
        }
    }

    private sealed class StubChannel(
        string name,
        IReadOnlyList<string> adminTargets,
        ExtChannelSendResult result) : IChannelService
    {
        public string Name => name;

        public HeartbeatService? HeartbeatService { get; set; }

        public CronService? CronService { get; set; }

        public IApprovalService? ApprovalService => null;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public IReadOnlyList<string> GetAdminTargets() => adminTargets;

        public Task<ExtChannelSendResult> DeliverAsync(
            string target,
            ChannelOutboundMessage message,
            object? metadata = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(result);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
