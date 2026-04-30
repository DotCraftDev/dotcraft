namespace DotCraft.Configuration;

/// <summary>
/// Applies process-local runtime configuration overrides that must not be persisted
/// back to workspace config files.
/// </summary>
public static class RuntimeConfigOverrides
{
    public const string ManagedProxyEndpoint = "DOTCRAFT_MANAGED_PROXY_ENDPOINT";
    public const string ManagedProxyApiKey = "DOTCRAFT_MANAGED_PROXY_API_KEY";

    public static bool HasManagedProxyOverride =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ManagedProxyEndpoint))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ManagedProxyApiKey));

    public static void ApplyManagedProxy(AppConfig config)
    {
        var endpoint = Environment.GetEnvironmentVariable(ManagedProxyEndpoint);
        var apiKey = Environment.GetEnvironmentVariable(ManagedProxyApiKey);
        if (!string.IsNullOrWhiteSpace(endpoint))
            config.EndPoint = endpoint;
        if (!string.IsNullOrWhiteSpace(apiKey))
            config.ApiKey = apiKey;
    }
}
