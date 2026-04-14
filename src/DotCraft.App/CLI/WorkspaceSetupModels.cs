using DotCraft.Localization;

namespace DotCraft.CLI;

public enum WorkspaceBootstrapProfile
{
    Default,
    Developer,
    PersonalAssistant
}

public sealed record WorkspaceSetupRequest
{
    public required Language Language { get; init; }

    public required string Model { get; init; }

    public required string EndPoint { get; init; }

    public required string ApiKey { get; init; }

    public WorkspaceBootstrapProfile Profile { get; init; } = WorkspaceBootstrapProfile.Default;

    public bool SaveToUserConfig { get; init; }

    public bool PreferExistingUserConfig { get; init; }
}
