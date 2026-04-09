using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Spectre.Console;

namespace DotCraft.Mcp;

public sealed class McpServerStatusSnapshot
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string StartupState { get; set; } = "idle";
    public int ToolCount { get; set; }
    public int ResourceCount { get; set; }
    public int ResourceTemplateCount { get; set; }
    public string? LastError { get; set; }
    public string Transport { get; set; } = "stdio";
}

public sealed class McpServerStatusChangedEventArgs : EventArgs
{
    public McpServerStatusSnapshot Status { get; init; } = new();
}

public sealed class McpClientManager : IAsyncDisposable
{
    private sealed class ServerRuntimeState
    {
        public McpServerConfig Config { get; set; } = new();
        public McpClient? Client { get; set; }
        public McpServerStatusSnapshot Status { get; set; } = new();
        public List<McpClientTool> CachedTools { get; set; } = [];
    }

    private readonly ILogger<McpClientManager>? _logger;
    private readonly Dictionary<string, ServerRuntimeState> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<McpClientTool> _tools = [];
    private readonly Dictionary<string, string> _toolServerMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public IReadOnlyList<McpClientTool> Tools => _tools;
    public IReadOnlyDictionary<string, string> ToolServerMap => _toolServerMap;

    public event EventHandler<McpServerStatusChangedEventArgs>? StatusChanged;

    public McpClientManager(ILogger<McpClientManager>? logger = null)
    {
        _logger = logger;
    }

    public async Task ConnectAsync(IEnumerable<McpServerConfig> servers, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await DisposeAllClientsUnsafeAsync();
            _servers.Clear();

            foreach (var server in servers)
            {
                var clone = server.Clone();
                _servers[clone.Name] = new ServerRuntimeState
                {
                    Config = clone,
                    Status = CreateStatus(clone, clone.Enabled ? "idle" : "disabled")
                };
            }

            foreach (var state in _servers.Values.OrderBy(s => s.Config.Name, StringComparer.OrdinalIgnoreCase))
                await ConnectServerUnsafeAsync(state, cancellationToken);

            RebuildToolIndexUnsafe();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<McpServerConfig?> GetConfigAsync(string name, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return _servers.TryGetValue(name, out var state) ? state.Config.Clone() : null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<McpServerConfig>> ListConfigsAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return _servers.Values
                .Select(s => s.Config.Clone())
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<McpServerStatusSnapshot>> ListStatusesAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return _servers.Values
                .Select(s => CloneStatus(s.Status))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<McpServerStatusSnapshot> UpsertAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_servers.TryGetValue(config.Name, out var existing))
            {
                await DisposeClientUnsafeAsync(existing);
                existing.Config = config.Clone();
                existing.Status = CreateStatus(existing.Config, existing.Config.Enabled ? "idle" : "disabled");
                await ConnectServerUnsafeAsync(existing, cancellationToken);
            }
            else
            {
                var state = new ServerRuntimeState
                {
                    Config = config.Clone(),
                    Status = CreateStatus(config, config.Enabled ? "idle" : "disabled")
                };
                _servers[config.Name] = state;
                await ConnectServerUnsafeAsync(state, cancellationToken);
            }

            RebuildToolIndexUnsafe();
            return CloneStatus(_servers[config.Name].Status);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (!_servers.Remove(name, out var state))
                return false;

            await DisposeClientUnsafeAsync(state);
            RebuildToolIndexUnsafe();
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<McpServerStatusSnapshot> TestAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        var status = CreateStatus(config, config.Enabled ? "starting" : "disabled");
        if (!config.Enabled)
            return status;

        McpClient? client = null;
        try
        {
            client = await CreateClientAsync(config, cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            status.StartupState = "ready";
            status.ToolCount = tools.Count;
            return status;
        }
        catch (Exception ex)
        {
            status.StartupState = "error";
            status.LastError = ex.Message;
            return status;
        }
        finally
        {
            if (client != null)
            {
                try
                {
                    await client.DisposeAsync();
                }
                catch
                {
                    // Best-effort temp client cleanup
                }
            }
        }
    }

    private async Task ConnectServerUnsafeAsync(ServerRuntimeState state, CancellationToken cancellationToken)
    {
        state.Status = CreateStatus(state.Config, state.Config.Enabled ? "starting" : "disabled");
        OnStatusChanged(state.Status);

        if (!state.Config.Enabled)
        {
            state.CachedTools.Clear();
            return;
        }

        try
        {
            var client = await CreateClientAsync(state.Config, cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

            state.Client = client;
            state.CachedTools = [.. tools];
            state.Status = CreateStatus(state.Config, "ready");
            state.Status.ToolCount = state.CachedTools.Count;

            _logger?.LogInformation(
                "MCP connected to {ServerName} with {ToolCount} tools",
                state.Config.Name,
                tools.Count);
            AnsiConsole.MarkupLine(
                $"[grey][[MCP]][/] [green]Connected to {Markup.Escape(state.Config.Name)} ({tools.Count} tools)[/]");
        }
        catch (Exception ex)
        {
            state.CachedTools.Clear();
            state.Status = CreateStatus(state.Config, "error");
            state.Status.LastError = ex.Message;

            _logger?.LogError(ex, "MCP connection to {ServerName} failed", state.Config.Name);
            AnsiConsole.MarkupLine(
                $"[grey][[MCP]][/] [red]Failed to connect to {Markup.Escape(state.Config.Name)}: {Markup.Escape(ex.Message)}[/]");
        }

        OnStatusChanged(state.Status);
    }

    private void RebuildToolIndexUnsafe()
    {
        _tools.Clear();
        _toolServerMap.Clear();

        foreach (var state in _servers.Values
                     .Where(s => s.Status.StartupState == "ready")
                     .OrderBy(s => s.Config.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var tool in state.CachedTools)
            {
                _tools.Add(tool);
                _toolServerMap[tool.Name] = state.Config.Name;
            }
            state.Status.ToolCount = state.CachedTools.Count;
        }
    }

    private static McpServerStatusSnapshot CreateStatus(McpServerConfig config, string startupState) =>
        new()
        {
            Name = config.Name,
            Enabled = config.Enabled,
            StartupState = startupState,
            ToolCount = 0,
            ResourceCount = 0,
            ResourceTemplateCount = 0,
            LastError = null,
            Transport = config.NormalizedTransport
        };

    private static McpServerStatusSnapshot CloneStatus(McpServerStatusSnapshot status) =>
        new()
        {
            Name = status.Name,
            Enabled = status.Enabled,
            StartupState = status.StartupState,
            ToolCount = status.ToolCount,
            ResourceCount = status.ResourceCount,
            ResourceTemplateCount = status.ResourceTemplateCount,
            LastError = status.LastError,
            Transport = status.Transport
        };

    private void OnStatusChanged(McpServerStatusSnapshot status)
    {
        StatusChanged?.Invoke(this, new McpServerStatusChangedEventArgs
        {
            Status = CloneStatus(status)
        });
    }

    private async Task DisposeClientUnsafeAsync(ServerRuntimeState state)
    {
        if (state.Client == null)
            return;

        try
        {
            await state.Client.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MCP client disposal error");
        }
        finally
        {
            state.Client = null;
            state.CachedTools.Clear();
        }
    }

    private async Task DisposeAllClientsUnsafeAsync()
    {
        foreach (var state in _servers.Values)
            await DisposeClientUnsafeAsync(state);
    }

    private static async Task<McpClient> CreateClientAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        IClientTransport transport;

        if (server.NormalizedTransport == "streamableHttp")
        {
            if (string.IsNullOrWhiteSpace(server.Url))
                throw new InvalidOperationException($"MCP server '{server.Name}' has transport 'streamableHttp' but no Url configured.");

            var options = new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Url),
                Name = server.Name,
            };

            var headers = new Dictionary<string, string>(server.Headers, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(server.BearerTokenEnvVar))
            {
                var token = Environment.GetEnvironmentVariable(server.BearerTokenEnvVar);
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException($"MCP server '{server.Name}' requires env var '{server.BearerTokenEnvVar}'.");
                headers["Authorization"] = $"Bearer {token}";
            }

            foreach (var (headerName, envVarName) in server.EnvHttpHeaders)
            {
                var value = Environment.GetEnvironmentVariable(envVarName);
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"MCP server '{server.Name}' requires env var '{envVarName}' for header '{headerName}'.");
                headers[headerName] = value;
            }

            if (headers.Count > 0)
                options.AdditionalHeaders = headers;

            transport = new HttpClientTransport(options);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(server.Command))
                throw new InvalidOperationException($"MCP server '{server.Name}' has transport 'stdio' but no Command configured.");

            var environmentVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in server.EnvironmentVariables)
                environmentVariables[key] = value;

            foreach (var envVar in server.EnvVars.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                environmentVariables[envVar] = Environment.GetEnvironmentVariable(envVar);
            }

            var options = new StdioClientTransportOptions
            {
                Command = server.Command,
                Name = server.Name,
                Arguments = server.Arguments is { Count: > 0 } ? server.Arguments : null,
                EnvironmentVariables = environmentVariables.Count > 0 ? environmentVariables : null
            };

            if (!string.IsNullOrWhiteSpace(server.Cwd))
            {
                var prop = typeof(StdioClientTransportOptions).GetProperty("WorkingDirectory")
                    ?? typeof(StdioClientTransportOptions).GetProperty("CurrentDirectory");
                if (prop is { CanWrite: true })
                    prop.SetValue(options, server.Cwd);
            }

            transport = new StdioClientTransport(options);
        }

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _mutex.WaitAsync();
        try
        {
            await DisposeAllClientsUnsafeAsync();
            _servers.Clear();
            _tools.Clear();
            _toolServerMap.Clear();
        }
        finally
        {
            _mutex.Release();
            _mutex.Dispose();
        }
    }
}
