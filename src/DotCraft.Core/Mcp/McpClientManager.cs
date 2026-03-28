using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Spectre.Console;

namespace DotCraft.Mcp;

public sealed class McpClientManager : IAsyncDisposable
{
    private readonly ILogger<McpClientManager>? _logger;

    private readonly List<McpClient> _clients = [];
    
    private readonly List<McpClientTool> _tools = [];
    
    private readonly Dictionary<string, string> _toolServerMap = new();

    public IReadOnlyList<McpClientTool> Tools => _tools;

    public IReadOnlyDictionary<string, string> ToolServerMap => _toolServerMap;

    public McpClientManager(ILogger<McpClientManager>? logger = null)
    {
        _logger = logger;
    }

    public async Task ConnectAsync(IEnumerable<McpServerConfig> servers, CancellationToken cancellationToken = default)
    {
        foreach (var server in servers)
        {
            if (!server.Enabled)
                continue;

            try
            {
                var client = await CreateClientAsync(server, cancellationToken);
                _clients.Add(client);

                var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                foreach (var tool in tools)
                {
                    _tools.Add(tool);
                    _toolServerMap[tool.Name] = server.Name;
                }

                _logger?.LogInformation(
                    "MCP connected to {ServerName} with {ToolCount} tools",
                    server.Name,
                    tools.Count);
                AnsiConsole.MarkupLine(
                    $"[grey][[MCP]][/] [green]Connected to {Markup.Escape(server.Name)} ({tools.Count} tools)[/]");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "MCP connection to {ServerName} failed", server.Name);
                AnsiConsole.MarkupLine(
                    $"[grey][[MCP]][/] [red]Failed to connect to {Markup.Escape(server.Name)}: {Markup.Escape(ex.Message)}[/]");
            }
        }
    }

    private static async Task<McpClient> CreateClientAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        IClientTransport transport;

        if (server.Transport.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(server.Url))
                throw new InvalidOperationException($"MCP server '{server.Name}' has transport 'http' but no Url configured.");

            var options = new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Url),
                Name = server.Name,
            };

            if (server.Headers is { Count: > 0 })
            {
                options.AdditionalHeaders = new Dictionary<string, string>(server.Headers);
            }

            transport = new HttpClientTransport(options);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(server.Command))
                throw new InvalidOperationException($"MCP server '{server.Name}' has transport 'stdio' but no Command configured.");

            var options = new StdioClientTransportOptions
            {
                Command = server.Command,
                Name = server.Name,
            };

            if (server.Arguments is { Count: > 0 })
                options.Arguments = server.Arguments;

            if (server.EnvironmentVariables is { Count: > 0 })
                options.EnvironmentVariables = new Dictionary<string, string?>(
                    server.EnvironmentVariables.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));

            transport = new StdioClientTransport(options);
        }

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "MCP client disposal error");
            }
        }
        _clients.Clear();
        _tools.Clear();
        _toolServerMap.Clear();
    }
}
