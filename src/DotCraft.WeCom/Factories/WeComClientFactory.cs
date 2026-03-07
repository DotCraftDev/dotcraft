using DotCraft.Abstractions;
using Spectre.Console;

namespace DotCraft.WeCom.Factories;

/// <summary>
/// Factory for creating WeCom bot registry and permission service instances.
/// </summary>
public sealed class WeComClientFactory
{
    /// <summary>
    /// Creates a WeComBotRegistry instance from configuration.
    /// </summary>
    public static WeComBotRegistry CreateRegistry(ModuleContext context)
    {
        var config = context.Config.WeComBot;
        var registry = new WeComBotRegistry();

        foreach (var robotConfig in config.Robots)
        {
            if (string.IsNullOrEmpty(robotConfig.Token) || string.IsNullOrEmpty(robotConfig.AesKey))
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Skipping WeCom bot {Markup.Escape(robotConfig.Path)}: Token or AesKey is empty[/]");
                continue;
            }

            registry.Register(
                path: robotConfig.Path,
                token: robotConfig.Token,
                encodingAesKey: robotConfig.AesKey);
        }

        if (config.DefaultRobot != null &&
            !string.IsNullOrEmpty(config.DefaultRobot.Token) &&
            !string.IsNullOrEmpty(config.DefaultRobot.AesKey))
        {
            AnsiConsole.MarkupLine("[grey][[WeCom]][/] [green]Default robot configured[/]");
        }

        return registry;
    }

    /// <summary>
    /// Creates a WeComPermissionService instance from configuration.
    /// </summary>
    public static WeComPermissionService CreatePermissionService(ModuleContext context)
    {
        var config = context.Config.WeComBot;
        return new WeComPermissionService(
            config.AdminUsers.Select(id => id.ToString()),
            config.WhitelistedUsers.Select(id => id.ToString()),
            config.WhitelistedChats);
    }
}
