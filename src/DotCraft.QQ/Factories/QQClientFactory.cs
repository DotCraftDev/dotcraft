using DotCraft.Abstractions;
using Spectre.Console;

namespace DotCraft.QQ.Factories;

/// <summary>
/// Factory for creating QQ bot client and permission service instances.
/// </summary>
public sealed class QQClientFactory
{
    /// <summary>
    /// Creates a QQBotClient instance from configuration.
    /// </summary>
    public static QQBotClient CreateClient(ModuleContext context)
    {
        var config = context.Config.QQBot;
        var qqToken = string.IsNullOrEmpty(config.AccessToken) ? null : config.AccessToken;
        var client = new QQBotClient(config.Host, config.Port, qqToken);
        client.OnLog += msg => AnsiConsole.MarkupLine($"[grey][[QQ]] {Markup.Escape(msg)}[/]");
        return client;
    }

    /// <summary>
    /// Creates a QQPermissionService instance from configuration.
    /// </summary>
    public static QQPermissionService CreatePermissionService(ModuleContext context)
    {
        var config = context.Config.QQBot;
        return new QQPermissionService(
            config.AdminUsers,
            config.WhitelistedUsers,
            config.WhitelistedGroups);
    }
}
