using DotCraft.Configuration;

namespace DotCraft.Api;

/// <summary>
/// Validator for API configuration.
/// </summary>
public sealed class ApiConfigValidator : IModuleConfigValidator<ApiConfig>
{
    /// <inheritdoc />
    public IReadOnlyList<string> Validate(ApiConfig config)
    {
        var errors = new List<string>();

        if (config.Enabled)
        {
            if (config.Port <= 0 || config.Port > 65535)
            {
                errors.Add($"Invalid port number: {config.Port}");
            }

            // Note: ApiKey is optional for API mode (can be empty for open access)
            if (!string.IsNullOrEmpty(config.ApiKey) && config.ApiKey.Length < 8)
            {
                errors.Add("API key should be at least 8 characters long for security");
            }
        }

        return errors;
    }
}
