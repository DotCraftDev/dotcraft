using DotCraft.Protocol.AppServer;

namespace DotCraft.GitHubTracker.Protocol.AppServer;

public sealed class GitHubTrackerAppServerExtension(
    GitHubTrackerConfigProtocolService configService) : IAppServerProtocolExtension
{
    public IReadOnlyCollection<string> Methods { get; } =
        [GitHubTrackerAppServerMethods.Get, GitHubTrackerAppServerMethods.Update];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.WorkspaceCraftPath))
            return;

        builder.Capabilities.GitHubTrackerConfig = true;
        builder.SetExtension("githubTrackerConfig", true);
    }

    public Task<object?> HandleAsync(AppServerIncomingMessage msg, AppServerExtensionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.WorkspaceCraftPath))
            throw AppServerErrors.MethodNotFound(msg.Method ?? string.Empty);

        return msg.Method switch
        {
            GitHubTrackerAppServerMethods.Get => HandleGetAsync(context.WorkspaceCraftPath),
            GitHubTrackerAppServerMethods.Update => HandleUpdateAsync(msg, context.WorkspaceCraftPath),
            _ => throw AppServerErrors.MethodNotFound(msg.Method ?? string.Empty)
        };
    }

    private Task<object?> HandleGetAsync(string workspaceCraftPath)
    {
        var config = configService.LoadWorkspaceConfig(workspaceCraftPath);
        return Task.FromResult<object?>(new GitHubTrackerGetResult
        {
            Config = configService.MaskConfig(config)
        });
    }

    private Task<object?> HandleUpdateAsync(AppServerIncomingMessage msg, string workspaceCraftPath)
    {
        var p = GitHubTrackerConfigProtocolService.GetParams<GitHubTrackerUpdateParams>(msg);
        var existing = configService.LoadWorkspaceConfig(workspaceCraftPath);
        var config = configService.NormalizeConfig(p.Config);
        config = configService.PreserveMaskedApiKey(config, existing);
        configService.ValidateConfig(config);
        configService.SaveWorkspaceConfig(workspaceCraftPath, config);

        return Task.FromResult<object?>(new GitHubTrackerUpdateResult
        {
            Config = configService.MaskConfig(config)
        });
    }
}
