using DotCraft.Configuration;

namespace DotCraft.WeCom;

/// <summary>
/// Validator for WeCom bot configuration.
/// </summary>
public sealed class WeComConfigValidator : IModuleConfigValidator<WeComBotConfig>
{
    /// <inheritdoc />
    public IReadOnlyList<string> Validate(WeComBotConfig config)
    {
        var errors = new List<string>();

        if (config.Enabled)
        {
            if (config.Port <= 0 || config.Port > 65535)
            {
                errors.Add($"Invalid port number: {config.Port}");
            }

            // Validate robot configurations
            foreach (var robot in config.Robots)
            {
                if (string.IsNullOrEmpty(robot.Token))
                {
                    errors.Add($"Robot at path '{robot.Path}' is missing Token");
                }
            }

            if (config.DefaultRobot != null && string.IsNullOrEmpty(config.DefaultRobot.Token))
            {
                errors.Add("Default robot is missing Token");
            }
        }

        return errors;
    }
}
