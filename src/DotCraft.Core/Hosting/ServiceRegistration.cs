using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Hooks;
using DotCraft.Tracing;
using DotCraft.Logging;
using DotCraft.Sessions;
using DotCraft.Sessions.Protocol;
using DotCraft.Localization;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Skills;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hosting;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers all core DotCraft services into the DI container.
    /// </summary>
    public static IServiceCollection AddDotCraft(
        this IServiceCollection services,
        AppConfig config,
        string workspacePath,
        string botPath)
    {
        services.AddLogging(builder =>
        {
            var loggingCfg = config.Logging;
            var minLevel = Enum.TryParse<LogLevel>(loggingCfg.MinLevel, ignoreCase: true, out var lvl)
                ? lvl
                : LogLevel.Information;
            builder.SetMinimumLevel(minLevel);

            if (loggingCfg.Enabled)
            {
                var logsDir = Path.Combine(botPath, loggingCfg.Directory);
                builder.AddProvider(new FileLoggerProvider(logsDir, minLevel, loggingCfg.RetentionDays));
            }

            if (loggingCfg.Console)
            {
                builder.AddConsole();
            }
        });
        services.AddSingleton(config);
        services.AddSingleton(new DotCraftPaths
        {
            WorkspacePath = workspacePath,
            CraftPath = botPath
        });
        services.AddSingleton(new PathBlacklist(config.Security.BlacklistedPaths));
        services.AddSingleton(new MemoryStore(botPath));
        services.AddSingleton(new SessionStore(botPath, config.CompactSessions));
        services.AddSingleton(new ApprovalStore(botPath));
        var skillsLoader = new SkillsLoader(botPath);
        skillsLoader.DeployBuiltInSkills();
        services.AddSingleton(skillsLoader);

        var customCommandLoader = new CustomCommandLoader(botPath);
        customCommandLoader.DeployBuiltInCommands();
        services.AddSingleton(customCommandLoader);

        services.AddSingleton(new LanguageService(config.Language));

        var cronStorePath = Path.Combine(botPath, config.Cron.StorePath);
        services.AddSingleton(new CronService(cronStorePath));
        services.AddSingleton<CronTools>(sp => new CronTools(sp.GetRequiredService<CronService>()));

        // Hooks
        if (config.Hooks.Enabled)
        {
            var hooksLoader = new HooksLoader(botPath);
            var hooksConfig = hooksLoader.Load();
            var hookRunner = new HookRunner(hooksConfig, workspacePath);
            services.AddSingleton(hookRunner);
        }

        services.AddSingleton<McpClientManager>();
        services.AddSingleton(new SessionGate(config.MaxSessionQueueSize));
        services.AddSingleton<ActiveRunRegistry>();
        services.AddSingleton(new ThreadStore(botPath));

        // Register configuration validation
        services.AddConfigurationValidation();

        if (config.Tracing.Enabled)
        {
            var tracingStoragePath = Path.Combine(botPath, "tracing");
            var traceStore = new TraceStore(tracingStoragePath);
            traceStore.LoadFromDisk();
            services.AddSingleton(traceStore);
            services.AddSingleton<TraceCollector>();

            var tokenUsageStore = new TokenUsageStore(tracingStoragePath);
            tokenUsageStore.LoadFromDisk();
            services.AddSingleton(tokenUsageStore);
        }

        return services;
    }

    /// <summary>
    /// Validates module configurations and prints diagnostics.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <param name="moduleRegistry">The module registry whose modules provide their own validators.</param>
    /// <returns>True if all configurations are valid.</returns>
    public static bool ValidateConfigurations(AppConfig config, ModuleRegistry moduleRegistry)
    {
        var validator = new ConfigValidator(moduleRegistry);
        return validator.ValidateAndLogErrors(config);
    }
}

/// <summary>
/// Extension methods for IServiceProvider.
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    /// Initializes async services.
    /// </summary>
    public static async Task InitializeServicesAsync(this IServiceProvider provider)
    {
        var config = provider.GetRequiredService<AppConfig>();
        var mcpManager = provider.GetRequiredService<McpClientManager>();
        if (config.McpServers.Count > 0)
        {
            await mcpManager.ConnectAsync(config.McpServers);
        }
    }

    /// <summary>
    /// Disposes async services.
    /// </summary>
    public static async ValueTask DisposeServicesAsync(this IServiceProvider provider)
    {
        var cronService = provider.GetRequiredService<CronService>();
        cronService.Stop();
        cronService.Dispose();

        var mcpManager = provider.GetRequiredService<McpClientManager>();
        await mcpManager.DisposeAsync();
    }
}
