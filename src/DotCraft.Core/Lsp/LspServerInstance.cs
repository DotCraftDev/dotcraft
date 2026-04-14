using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DotCraft.Lsp;

public sealed class LspServerInstance
{
    private const int ContentModifiedErrorCode = -32801;
    private const int MaxTransientRetries = 3;
    private const int RetryBaseDelayMs = 500;

    private readonly string _workspacePath;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly List<(string Method, Action<JsonElement> Handler)> _notificationHandlers = [];
    private readonly List<(string Method, Func<JsonElement, Task<object?>> Handler)> _requestHandlers = [];

    private LspJsonRpcClient? _client;
    private LspServerState _state = LspServerState.Stopped;
    private DateTimeOffset? _startTime;
    private Exception? _lastError;
    private int _restartCount;
    private int _crashRecoveryCount;

    public LspServerInstance(string name, LspServerConfig config, string workspacePath, ILogger? logger = null)
    {
        Name = name;
        Config = config;
        _workspacePath = workspacePath;
        _logger = logger;
    }

    public string Name { get; }

    public LspServerConfig Config { get; }

    public LspServerState State => _state;

    public DateTimeOffset? StartTime => _startTime;

    public Exception? LastError => _lastError;

    public int RestartCount => _restartCount;

    public bool IsHealthy() => _state == LspServerState.Running && _client is { IsStarted: true };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state is LspServerState.Running or LspServerState.Starting)
                return;

            var maxRestarts = Config.MaxRestarts ?? 3;
            if (_state == LspServerState.Error && _crashRecoveryCount > maxRestarts)
            {
                throw new InvalidOperationException(
                    $"LSP server '{Name}' exceeded max crash recovery attempts ({maxRestarts}).");
            }

            _state = LspServerState.Starting;
            _lastError = null;

            var client = new LspJsonRpcClient(Name, _logger);
            _client = client;

            foreach (var (method, handler) in _notificationHandlers)
                client.OnNotification(method, handler);
            foreach (var (method, handler) in _requestHandlers)
                client.OnRequest(method, handler);

            try
            {
                await client.StartAsync(
                    Config.Command,
                    Config.Arguments,
                    Config.EnvironmentVariables,
                    Config.WorkspaceFolder,
                    cancellationToken);

                var workspaceFolder = string.IsNullOrWhiteSpace(Config.WorkspaceFolder)
                    ? _workspacePath
                    : Path.GetFullPath(Config.WorkspaceFolder);
                var workspaceUri = LspUriHelpers.ToFileUri(workspaceFolder);

                var initializationOptions = Config.InitializationOptions.HasValue
                    ? (object)Config.InitializationOptions.Value
                    : new { };

                var initializeParams = new
                {
                    processId = Environment.ProcessId,
                    rootPath = workspaceFolder,
                    rootUri = workspaceUri,
                    workspaceFolders = new[]
                    {
                        new
                        {
                            uri = workspaceUri,
                            name = Path.GetFileName(workspaceFolder)
                        }
                    },
                    initializationOptions,
                    capabilities = new
                    {
                        workspace = new
                        {
                            configuration = false,
                            workspaceFolders = false
                        },
                        textDocument = new
                        {
                            synchronization = new
                            {
                                dynamicRegistration = false,
                                willSave = false,
                                willSaveWaitUntil = false,
                                didSave = true
                            },
                            hover = new
                            {
                                dynamicRegistration = false
                            },
                            definition = new
                            {
                                dynamicRegistration = false,
                                linkSupport = true
                            },
                            references = new
                            {
                                dynamicRegistration = false
                            },
                            documentSymbol = new
                            {
                                dynamicRegistration = false,
                                hierarchicalDocumentSymbolSupport = true
                            },
                            callHierarchy = new
                            {
                                dynamicRegistration = false
                            }
                        }
                    }
                };

                var startupTimeoutMs = Config.StartupTimeoutMs ?? 30_000;
                await client.SendRequestAsync(
                    "initialize",
                    initializeParams,
                    TimeSpan.FromMilliseconds(startupTimeoutMs),
                    cancellationToken);

                await client.SendNotificationAsync("initialized", new { }, cancellationToken);

                if (Config.Settings.HasValue)
                {
                    await client.SendNotificationAsync(
                        "workspace/didChangeConfiguration",
                        new
                        {
                            settings = Config.Settings.Value
                        },
                        cancellationToken);
                }

                _state = LspServerState.Running;
                _startTime = DateTimeOffset.UtcNow;
                _crashRecoveryCount = 0;
            }
            catch (Exception ex)
            {
                _state = LspServerState.Error;
                _lastError = ex;
                _crashRecoveryCount++;
                await StopClientQuietlyAsync();
                throw;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state is LspServerState.Stopped or LspServerState.Stopping)
                return;

            _state = LspServerState.Stopping;
            await StopClientQuietlyAsync();
            _state = LspServerState.Stopped;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        _restartCount++;
        await StartAsync(cancellationToken);
    }

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? @params,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsHealthy() || _client == null)
            throw new InvalidOperationException(
                $"Cannot send request to LSP server '{Name}': server is {_state}.");

        Exception? lastError = null;
        for (var attempt = 0; attempt <= MaxTransientRetries; attempt++)
        {
            try
            {
                return await _client.SendRequestAsync(method, @params, timeout, cancellationToken);
            }
            catch (LspJsonRpcClient.LspProtocolException ex)
                when (ex.ErrorCode == ContentModifiedErrorCode && attempt < MaxTransientRetries)
            {
                lastError = ex;
                var delay = RetryBaseDelayMs * (int)Math.Pow(2, attempt);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        _state = LspServerState.Error;
        _lastError = lastError;
        throw new InvalidOperationException(
            $"LSP request '{method}' failed for server '{Name}': {lastError?.Message ?? "unknown error"}",
            lastError);
    }

    public async Task SendNotificationAsync(
        string method,
        object? @params,
        CancellationToken cancellationToken = default)
    {
        if (!IsHealthy() || _client == null)
            throw new InvalidOperationException(
                $"Cannot send notification to LSP server '{Name}': server is {_state}.");

        await _client.SendNotificationAsync(method, @params, cancellationToken);
    }

    public void OnNotification(string method, Action<JsonElement> handler)
    {
        _notificationHandlers.Add((method, handler));
        _client?.OnNotification(method, handler);
    }

    public void OnRequest(string method, Func<JsonElement, Task<object?>> handler)
    {
        _requestHandlers.Add((method, handler));
        _client?.OnRequest(method, handler);
    }

    private async Task StopClientQuietlyAsync()
    {
        if (_client == null)
            return;

        try
        {
            await _client.StopAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Stopping LSP server client failed for {Server}", Name);
        }
        finally
        {
            _client = null;
        }
    }
}
