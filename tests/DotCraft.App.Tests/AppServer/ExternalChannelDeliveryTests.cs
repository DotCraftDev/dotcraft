using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.ExternalChannel;
using DotCraft.Protocol;
using DotCraft.Modules;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.AppServer;

public sealed class ExternalChannelDeliveryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ExternalChannelDeliveryTests_" + Guid.NewGuid().ToString("N")[..8]);

    public ExternalChannelDeliveryTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ChannelMediaResolver_RejectsTextMessageWithSource()
    {
        var store = new FileSystemChannelMediaArtifactStore(_tempDir);
        var resolver = new ChannelMediaResolver(store, Path.Combine(_tempDir, "tmp"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(new ChannelOutboundMessage
        {
            Kind = "text",
            Text = "hello",
            Source = new ChannelMediaSource
            {
                Kind = "hostPath",
                HostPath = "x.txt"
            }
        }));

        Assert.Contains("Text delivery", ex.Message);
    }

    [Fact]
    public async Task ChannelMediaResolver_RejectsSourceWithMultipleFields()
    {
        var store = new FileSystemChannelMediaArtifactStore(_tempDir);
        var resolver = new ChannelMediaResolver(store, Path.Combine(_tempDir, "tmp"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(new ChannelOutboundMessage
        {
            Kind = "file",
            Source = new ChannelMediaSource
            {
                Kind = "hostPath",
                HostPath = "a.txt",
                Url = "https://example.com/a.txt"
            }
        }));

        Assert.Contains("exactly one", ex.Message);
    }

    [Fact]
    public async Task ChannelMediaResolver_RegistersHostPathArtifact_AndArtifactIdCanResolve()
    {
        var path = Path.Combine(_tempDir, "report.txt");
        await File.WriteAllTextAsync(path, "report");

        var store = new FileSystemChannelMediaArtifactStore(_tempDir);
        var resolver = new ChannelMediaResolver(store, Path.Combine(_tempDir, "tmp"));

        var first = await resolver.ResolveAsync(new ChannelOutboundMessage
        {
            Kind = "file",
            Source = new ChannelMediaSource
            {
                Kind = "hostPath",
                HostPath = path
            }
        });

        var second = await resolver.ResolveAsync(new ChannelOutboundMessage
        {
            Kind = "file",
            Source = new ChannelMediaSource
            {
                Kind = "artifactId",
                ArtifactId = first.Artifact.Id
            }
        });

        Assert.Equal(first.Artifact.Id, second.Artifact.Id);
        Assert.Equal(path, second.Artifact.ResolvedPath);
    }

    [Fact]
    public async Task ExternalChannelMessageDispatcher_RejectsUnsupportedUrlSource()
    {
        var store = new FileSystemChannelMediaArtifactStore(_tempDir);
        var resolver = new ChannelMediaResolver(store, Path.Combine(_tempDir, "tmp"));
        var dispatcher = new ExternalChannelMessageDispatcher(resolver, store);
        var transport = new StubTransport();
        var connection = CreateAdapterConnection(structuredDelivery: true, fileConstraints: new ChannelMediaConstraints
        {
            SupportsUrl = false,
            SupportsBase64 = true
        });

        var result = await dispatcher.DeliverAsync(
            transport,
            connection,
            "telegram",
            "group:1",
            new ChannelOutboundMessage
            {
                Kind = "file",
                Source = new ChannelMediaSource
                {
                    Kind = "url",
                    Url = "https://example.com/file.pdf"
                }
            },
            metadata: null);

        Assert.False(result.Delivered);
        Assert.Equal("UnsupportedMediaSource", result.ErrorCode);
    }

    [Fact]
    public async Task ExternalChannelMessageDispatcher_CleansUpTemporaryBase64Artifact()
    {
        var mediaRoot = Path.Combine(_tempDir, "media");
        var store = new FileSystemChannelMediaArtifactStore(mediaRoot);
        var resolver = new ChannelMediaResolver(store, Path.Combine(mediaRoot, "tmp"));
        var dispatcher = new ExternalChannelMessageDispatcher(resolver, store);
        var transport = new StubTransport(new ExtChannelSendResult { Delivered = true });
        var connection = CreateAdapterConnection(structuredDelivery: true, fileConstraints: new ChannelMediaConstraints
        {
            SupportsBase64 = true
        });

        var result = await dispatcher.DeliverAsync(
            transport,
            connection,
            "feishu",
            "group:1",
            new ChannelOutboundMessage
            {
                Kind = "file",
                FileName = "report.txt",
                Source = new ChannelMediaSource
                {
                    Kind = "dataBase64",
                    DataBase64 = Convert.ToBase64String("hello"u8.ToArray())
                }
            },
            metadata: null);

        Assert.True(result.Delivered);
        var tmpDir = Path.Combine(mediaRoot, "tmp");
        Assert.False(Directory.Exists(tmpDir) && Directory.EnumerateFiles(tmpDir).Any());
    }

    [Fact]
    public async Task ExternalChannelMessageDispatcher_MapsLegacyErrorResponse()
    {
        var transport = new StubTransport(new { delivered = false, error = "legacy failed" });
        var store = new FileSystemChannelMediaArtifactStore(_tempDir);
        var resolver = new ChannelMediaResolver(store, Path.Combine(_tempDir, "tmp"));
        var dispatcher = new ExternalChannelMessageDispatcher(resolver, store);
        var connection = CreateAdapterConnection(structuredDelivery: false, fileConstraints: null, supportsDelivery: true);

        var result = await dispatcher.DeliverAsync(
            transport,
            connection,
            "telegram",
            "group:1",
            new ChannelOutboundMessage
            {
                Kind = "text",
                Text = "hello"
            },
            metadata: null);

        Assert.False(result.Delivered);
        Assert.Equal("AdapterDeliveryFailed", result.ErrorCode);
        Assert.Equal("legacy failed", result.ErrorMessage);
    }

    [Fact]
    public async Task ExternalChannelHost_DeliverAsync_WhenDisconnected_ReturnsFailure()
    {
        var host = new ExternalChannelHost(
            new ExternalChannelEntry
            {
                Name = "telegram",
                Enabled = true,
                Transport = ExternalChannelTransport.Subprocess,
                Command = "python"
            },
            new FakeSessionService(),
            "0.0.1-test",
            new ModuleRegistry(),
            _tempDir);

        var result = await host.DeliverAsync(
            "group:1",
            new ChannelOutboundMessage
            {
                Kind = "text",
                Text = "hello"
            });

        Assert.False(result.Delivered);
        Assert.Equal("AdapterDeliveryFailed", result.ErrorCode);
    }

    [Fact]
    public async Task ExternalChannelToolProvider_InjectsOnlyMatchingChannelTools()
    {
        var registry = new ExternalChannelRegistry();
        var host = CreateHost("telegram");
        var transport = new StubTransport(new ExtChannelToolCallResult
        {
            Success = true,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = "Document sent." }]
        });
        var connection = CreateToolAdapterConnection(
            "telegram",
            [
                new ChannelToolDescriptor
                {
                    Name = "telegramSendDocument",
                    Description = "Send a document to the current Telegram chat.",
                    RequiresChatContext = true,
                    InputSchema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["fileName"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("fileName")
                    }
                }
            ]);
        AttachFakeAdapter(host, transport, connection);
        registry.Register("telegram", host);

        var provider = new ExternalChannelToolProvider(registry);
        var thread = new SessionThread
        {
            Id = "thread_001",
            WorkspacePath = _tempDir,
            OriginChannel = "telegram",
            ChannelContext = "chat_123",
            Status = ThreadStatus.Active
        };

        var tools = provider.CreateToolsForThread(thread, new HashSet<string>(StringComparer.Ordinal));
        var otherTools = provider.CreateToolsForThread(
            new SessionThread
            {
                Id = "thread_002",
                WorkspacePath = _tempDir,
                OriginChannel = "feishu",
                Status = ThreadStatus.Active
            },
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Single(tools);
        Assert.Empty(otherTools);

        var fn = Assert.IsAssignableFrom<AIFunction>(tools[0]);
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        using var scope = ExternalChannelToolExecutionScope.Set(
            new ExternalChannelToolExecutionContext
            {
                ThreadId = thread.Id,
                TurnId = turn.Id,
                OriginChannel = thread.OriginChannel,
                ChannelContext = thread.ChannelContext,
                SenderId = "user_42",
                GroupId = "chat_123",
                Turn = turn,
                NextItemSequence = () => turn.Items.Count + 1,
                EmitItemStarted = _ => { },
                EmitItemCompleted = _ => { }
            });

        var result = await fn.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["fileName"] = "report.pdf"
            }),
            CancellationToken.None);

        Assert.Equal(AppServerMethods.ExtChannelToolCall, transport.LastMethod);
        var toolParams = Assert.IsType<ExtChannelToolCallParams>(transport.LastParams);
        Assert.Equal("telegramSendDocument", toolParams.Tool);
        Assert.Equal("chat_123", toolParams.Context.ChannelContext);
        Assert.Equal("user_42", toolParams.Context.SenderId);
        Assert.Single(turn.Items);
        Assert.Equal(ItemType.ExternalChannelToolCall, turn.Items[0].Type);
        Assert.NotNull(result);
    }

    [Fact]
    public void ExternalChannelToolProvider_RejectsInvalidSchema_And_NameConflicts()
    {
        var registry = new ExternalChannelRegistry();
        var firstHost = CreateHost("telegram");
        var secondHost = CreateHost("feishu");

        AttachFakeAdapter(firstHost, new StubTransport(), CreateToolAdapterConnection(
            "telegram",
            [
                new ChannelToolDescriptor
                {
                    Name = "sharedTool",
                    Description = "Valid descriptor.",
                    InputSchema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                },
                new ChannelToolDescriptor
                {
                    Name = "invalidTool",
                    Description = "Invalid descriptor.",
                    InputSchema = new JsonObject
                    {
                        ["type"] = "array"
                    }
                }
            ]));

        AttachFakeAdapter(secondHost, new StubTransport(), CreateToolAdapterConnection(
            "feishu",
            [
                new ChannelToolDescriptor
                {
                    Name = "sharedTool",
                    Description = "Conflicts with telegram.",
                    InputSchema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                }
            ]));

        registry.Register("telegram", firstHost);
        registry.Register("feishu", secondHost);

        var provider = new ExternalChannelToolProvider(registry);
        _ = provider.CreateToolsForThread(
            new SessionThread
            {
                Id = "thread_010",
                WorkspacePath = _tempDir,
                OriginChannel = "telegram",
                Status = ThreadStatus.Active
            },
            new HashSet<string>(["BuiltInConflict"], StringComparer.Ordinal));

        Assert.Empty(firstHost.AdapterConnection!.RegisteredChannelTools);
        Assert.Contains(firstHost.AdapterConnection.ChannelToolDiagnostics, d => d.ToolName == "invalidTool");
        Assert.Contains(firstHost.AdapterConnection.ChannelToolDiagnostics, d => d.ToolName == "sharedTool");
        Assert.Contains(secondHost.AdapterConnection!.ChannelToolDiagnostics, d => d.ToolName == "sharedTool");
    }

    [Fact]
    public async Task ExternalChannelToolProvider_RequiresChatContextBeforeDispatch()
    {
        var registry = new ExternalChannelRegistry();
        var host = CreateHost("telegram");
        var transport = new StubTransport(new ExtChannelToolCallResult
        {
            Success = true,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = "ok" }]
        });
        AttachFakeAdapter(host, transport, CreateToolAdapterConnection(
            "telegram",
            [
                new ChannelToolDescriptor
                {
                    Name = "telegramSendDocument",
                    Description = "Send a document to the current Telegram chat.",
                    RequiresChatContext = true,
                    InputSchema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["fileName"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("fileName")
                    }
                }
            ]));
        registry.Register("telegram", host);

        var provider = new ExternalChannelToolProvider(registry);
        var tools = provider.CreateToolsForThread(
            new SessionThread
            {
                Id = "thread_020",
                WorkspacePath = _tempDir,
                OriginChannel = "telegram",
                Status = ThreadStatus.Active
            },
            new HashSet<string>(StringComparer.Ordinal));

        var fn = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools));
        var turn = new SessionTurn
        {
            Id = "turn_020",
            ThreadId = "thread_020",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        using var scope = ExternalChannelToolExecutionScope.Set(
            new ExternalChannelToolExecutionContext
            {
                ThreadId = "thread_020",
                TurnId = "turn_020",
                OriginChannel = "telegram",
                Turn = turn,
                NextItemSequence = () => turn.Items.Count + 1,
                EmitItemStarted = _ => { },
                EmitItemCompleted = _ => { }
            });

        var result = await fn.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["fileName"] = "report.pdf"
            }),
            CancellationToken.None);

        Assert.Null(transport.LastMethod);
        var resultText = Assert.IsType<string>(result);
        Assert.Contains("MissingChatContext", resultText);
    }

    private static AppServerConnection CreateAdapterConnection(
        bool structuredDelivery,
        ChannelMediaConstraints? fileConstraints,
        bool supportsDelivery = true)
    {
        var connection = new AppServerConnection();
        connection.TryMarkInitialized(
            new AppServerClientInfo { Name = "adapter", Version = "1.0.0" },
            new AppServerClientCapabilities
            {
                ChannelAdapter = new ChannelAdapterCapability
                {
                    ChannelName = "telegram",
                    DeliverySupport = supportsDelivery,
                    DeliveryCapabilities = structuredDelivery
                        ? new ChannelDeliveryCapabilities
                        {
                            StructuredDelivery = true,
                            Media = new ChannelMediaCapabilitySet
                            {
                                File = fileConstraints
                            }
                        }
                        : null
                }
            });
        connection.MarkClientReady();
        return connection;
    }

    private static AppServerConnection CreateToolAdapterConnection(
        string channelName,
        IReadOnlyList<ChannelToolDescriptor> tools)
    {
        var connection = new AppServerConnection();
        connection.TryMarkInitialized(
            new AppServerClientInfo { Name = $"{channelName}-adapter", Version = "1.0.0" },
            new AppServerClientCapabilities
            {
                ChannelAdapter = new ChannelAdapterCapability
                {
                    ChannelName = channelName,
                    DeliverySupport = true,
                    ChannelTools = tools.ToList()
                }
            });
        connection.MarkClientReady();
        return connection;
    }

    private ExternalChannelHost CreateHost(string channelName)
        => new(
            new ExternalChannelEntry
            {
                Name = channelName,
                Enabled = true,
                Transport = ExternalChannelTransport.Subprocess,
                Command = "python"
            },
            new FakeSessionService(),
            "0.0.1-test",
            new ModuleRegistry(),
            _tempDir);

    private static void AttachFakeAdapter(
        ExternalChannelHost host,
        StubTransport transport,
        AppServerConnection connection)
    {
        typeof(ExternalChannelHost)
            .GetField("_transport", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(host, transport);
        typeof(ExternalChannelHost)
            .GetField("_connection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(host, connection);
    }

    private sealed class StubTransport(object? result = null) : IAppServerTransport
    {
        private readonly object? _result = result;

        public string? LastMethod { get; private set; }

        public object? LastParams { get; private set; }

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AppServerIncomingMessage> SendClientRequestAsync(string method, object? @params, CancellationToken ct = default, TimeSpan? timeout = null)
        {
            LastMethod = method;
            LastParams = @params;
            var payload = _result ?? new ExtChannelSendResult { Delivered = true };
            var json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, result = payload }, SessionWireJsonOptions.Default);
            var msg = JsonSerializer.Deserialize<AppServerIncomingMessage>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            })!;
            return Task.FromResult(msg);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSessionService : ISessionService
    {
        public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }
        public Action<string>? ThreadDeletedForBroadcast { get; set; }
        public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

        public Task<SessionThread> CreateThreadAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? threadId = null, string? displayName = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task PauseThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ArchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(SessionIdentity identity, bool includeArchived = false, IReadOnlyList<string>? crossChannelOrigins = null, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(string threadId, bool replayRecent = false, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubmitInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, ChatMessage[]? messages = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ResolveApprovalAsync(string threadId, string turnId, string requestId, SessionApprovalDecision decision, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateThreadConfigurationAsync(string threadId, ThreadConfiguration config, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
