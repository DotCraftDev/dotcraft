using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using DotCraft.Acp;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tests.Sessions.Protocol.AppServer;

namespace DotCraft.Tests.Acp;

/// <summary>
/// Integration test: <see cref="AcpBridgeHandler"/> over in-memory stdio pipes against the same
/// AppServer harness as <see cref="WireClientIntegrationTests"/>, with <see cref="WireAcpExtensionProxy"/>.
/// </summary>
public sealed class AcpBridgePipeIntegrationTests
{
    [Fact]
    public async Task AcpBridge_InitializeAndSessionNew_RoundTripsOverPipes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AcpBridgePipe_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var store = new ThreadStore(tempDir);
        var service = new TestableSessionService(store);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var ideToBridge = new Pipe();
        var bridgeToIde = new Pipe();

        var serverTransport = StdioTransport.Create(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        serverTransport.Start();

        var connection = new AppServerConnection();
        var wireAcp = new WireAcpExtensionProxy();
        var handler = new AppServerRequestHandler(
            service,
            connection,
            serverTransport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
            serverVersion: "0.0.1-test",
            hostWorkspacePath: tempDir,
            wireAcpExtensionProxy: wireAcp);

        var serverCts = new CancellationTokenSource();
        var serverLoop = Task.Run(() => WireClientIntegrationTestsRunServerLoop.RunAsync(serverTransport, connection, handler, serverCts.Token));

        await using var wire = new AppServerWireClient(
            serverToClient.Reader.AsStream(),
            clientToServer.Writer.AsStream());
        wire.Start();

        await using var acp = new AcpTransport(ideToBridge.Reader.AsStream(), bridgeToIde.Writer.AsStream());
        acp.StartReaderLoop();

        var bridgeCts = new CancellationTokenSource();
        var bridge = new AcpBridgeHandler(acp, wire, tempDir);
        var bridgeTask = Task.Run(() => bridge.RunAsync(bridgeCts.Token));

        try
        {
            await using var ideWriter = new StreamWriter(ideToBridge.Writer.AsStream(), Encoding.UTF8) { AutoFlush = true };
            using var ideReader = new StreamReader(bridgeToIde.Reader.AsStream(), Encoding.UTF8);

            const string initLine =
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":1,"
                + "\"clientCapabilities\":{\"fs\":{\"readTextFile\":true}},"
                + "\"clientInfo\":{\"name\":\"test-ide\",\"version\":\"1.0\"}}}";
            await ideWriter.WriteLineAsync(initLine);

            var initResponse = await ReadJsonLineAsync(ideReader);
            using var initDoc = JsonDocument.Parse(initResponse);
            Assert.Equal(1, initDoc.RootElement.GetProperty("id").GetInt32());
            Assert.True(initDoc.RootElement.TryGetProperty("result", out var initResult));
            Assert.Equal(AcpBridgeHandler.ProtocolVersion, initResult.GetProperty("protocolVersion").GetInt32());

            await ideWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{}}");

            var sessionResponse = await ReadJsonLineAsync(ideReader);
            using var sessionDoc = JsonDocument.Parse(sessionResponse);
            Assert.Equal(2, sessionDoc.RootElement.GetProperty("id").GetInt32());
            Assert.True(sessionDoc.RootElement.TryGetProperty("result", out var sessionResult));
            var sessionId = sessionResult.GetProperty("sessionId").GetString();
            Assert.NotNull(sessionId);
            Assert.StartsWith("thread_", sessionId);
        }
        finally
        {
            bridgeToIde.Writer.Complete();
            ideToBridge.Writer.Complete();
            bridgeCts.Cancel();
            try
            {
                await bridgeTask.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch
            {
                // Bridge may throw on cancel; ignore
            }

            await wire.DisposeAsync();
            clientToServer.Writer.Complete();
            serverToClient.Writer.Complete();
            serverCts.Cancel();
            try
            {
                await serverLoop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                /* ignore */
            }

            serverCts.Dispose();
            bridgeCts.Dispose();
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static async Task<string> ReadJsonLineAsync(StreamReader reader)
    {
        var line = await reader.ReadLineAsync();
        Assert.NotNull(line);
        return line;
    }
}

/// <summary>
/// Exposes the private <see cref="WireClientIntegrationTests"/> server loop for reuse without duplication.
/// </summary>
internal static class WireClientIntegrationTestsRunServerLoop
{
    public static async Task RunAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AppServerIncomingMessage? msg;
            try
            {
                msg = await transport.ReadMessageAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (msg == null)
                break;

            if (msg.IsNotification)
            {
                if (msg.Method == AppServerMethods.Initialized)
                    handler.HandleInitializedNotification();
                continue;
            }

            if (!msg.IsRequest)
                continue;

            _ = Task.Run(async () =>
            {
                object? result;
                try
                {
                    result = await handler.HandleRequestAsync(msg, ct);
                }
                catch (AppServerException ex)
                {
                    await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, ex.ToError()), ct);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    var err = AppServerErrors.InternalError(ex.Message).ToError();
                    await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, err), ct);
                    return;
                }

                if (result != null)
                    await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
            }, ct);
        }

        connection.CancelAllSubscriptions();
    }
}
