using DotCraft.Configuration;

namespace DotCraft.QQ;

/// <summary>
/// Validator for QQ bot configuration.
/// </summary>
public sealed class QQConfigValidator : IModuleConfigValidator<AppConfig.QQBotConfig>
{
    /// <inheritdoc />
    public IReadOnlyList<string> Validate(AppConfig.QQBotConfig config)
    {
        var errors = new List<string>();

        if (config.Enabled && string.IsNullOrEmpty(config.AccessToken))
        {
            errors.Add("AccessToken is required when QQ bot is enabled");
        }

        if (config.Enabled && (config.Port <= 0 || config.Port > 65535))
        {
            errors.Add($"Invalid port number: {config.Port}");
        }

        return errors;
    }
}
