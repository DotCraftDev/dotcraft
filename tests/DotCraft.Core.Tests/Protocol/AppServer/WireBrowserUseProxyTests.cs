using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;

namespace DotCraft.Core.Tests.Protocol.AppServer;

public sealed class WireBrowserUseProxyTests
{
    private sealed class StubTransport : IAppServerTransport
    {
        public string? LastMethod { get; private set; }
        public JsonElement? LastParams { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            LastMethod = method;
            LastParams = JsonSerializer.SerializeToElement(@params, SessionWireJsonOptions.Default);
            var response = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new
                {
                    resultText = "ok",
                    images = new[]
                    {
                        new { mediaType = "image/png", dataBase64 = Convert.ToBase64String([1, 2, 3]) }
                    },
                    logs = new[] { "log" }
                }
            }, SessionWireJsonOptions.Default);
            return Task.FromResult(JsonSerializer.Deserialize<AppServerIncomingMessage>(response, SessionWireJsonOptions.Default)!);
        }
    }

    [Fact]
    public void BindThread_UnbindTransport_ControlsAvailability()
    {
        var prev = TracingChatClient.CurrentSessionKey;
        try
        {
            var proxy = new WireBrowserUseProxy();
            var transport = new StubTransport();
            var connection = new AppServerConnection();
            Assert.True(connection.TryMarkInitialized(
                new AppServerClientInfo { Name = "desktop", Version = "1" },
                new AppServerClientCapabilities
                {
                    BrowserUse = new BrowserUseCapability
                    {
                        Backend = "desktop-webcontents"
                    }
                }));

            proxy.BindThread("thread-a", transport, connection);
            TracingChatClient.CurrentSessionKey = "thread-a";
            Assert.True(proxy.IsAvailable);

            proxy.UnbindTransport(transport);
            Assert.False(proxy.IsAvailable);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = prev;
        }
    }

    [Fact]
    public async Task EvaluateAsync_SendsThreadAndTurnParameters()
    {
        var prev = TracingChatClient.CurrentSessionKey;
        try
        {
            var proxy = new WireBrowserUseProxy();
            var transport = new StubTransport();
            var connection = new AppServerConnection();
            connection.TryMarkInitialized(
                new AppServerClientInfo { Name = "desktop", Version = "1" },
                new AppServerClientCapabilities
                {
                    BrowserUse = new BrowserUseCapability()
                });
            proxy.BindThread("thread-b", transport, connection);
            TracingChatClient.CurrentSessionKey = "thread-b";

            var result = await proxy.EvaluateAsync("return 1;", 5);

            Assert.NotNull(result);
            Assert.Equal("ok", result!.ResultText);
            Assert.Equal(AppServerMethods.ExtBrowserUseEvaluate, transport.LastMethod);
            Assert.True(transport.LastParams.HasValue);
            var p = transport.LastParams.Value;
            Assert.Equal("thread-b", p.GetProperty("threadId").GetString());
            Assert.True(p.TryGetProperty("turnId", out _));
            Assert.Equal("return 1;", p.GetProperty("code").GetString());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = prev;
        }
    }
}
