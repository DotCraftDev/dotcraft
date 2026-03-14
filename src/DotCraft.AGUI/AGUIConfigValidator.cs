using DotCraft.Configuration;

namespace DotCraft.AGUI;

/// <summary>
/// Validator for AG-UI channel configuration.
/// </summary>
public sealed class AGUIConfigValidator : IModuleConfigValidator<AgUiConfig>
{
    /// <inheritdoc />
    public IReadOnlyList<string> Validate(AgUiConfig config)
    {
        var errors = new List<string>();

        if (!config.Enabled)
            return errors;

        if (string.IsNullOrWhiteSpace(config.Path))
            errors.Add("AgUi.Path cannot be empty when AgUi is enabled.");

        if (config.Port <= 0 || config.Port > 65535)
            errors.Add($"AgUi.Port must be between 1 and 65535, got {config.Port}.");

        if (config.RequireAuth && string.IsNullOrWhiteSpace(config.ApiKey))
            errors.Add("AgUi.ApiKey is required when AgUi.RequireAuth is true.");

        return errors;
    }
}
