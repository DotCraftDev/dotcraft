using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.AppServer;

/// <summary>
/// AppServer module: exposes the DotCraft Session Wire Protocol over stdio (JSON-RPC 2.0 JSONL).
/// Enabled via the <c>app-server</c> subcommand or <c>AppServer.Enabled = true</c> in config.
/// </summary>
[DotCraftModule("app-server", Priority = 250, Description = "AppServer: JSON-RPC 2.0 Session Wire Protocol over stdio for multi-language client integration")]
public sealed partial class AppServerModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.GetSection<AppServerConfig>("AppServer").Enabled;
}

/// <summary>
/// Host factory for AppServer mode.
/// </summary>
[HostFactory("app-server")]
public sealed class AppServerHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotCraftHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<AppServerHost>(serviceProvider);
    }
}
