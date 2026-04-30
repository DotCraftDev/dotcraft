using System.Reflection;
using System.Security.Cryptography;
using DotCraft.Configuration;

namespace DotCraft.AppServer;

/// <summary>
/// Environment-variable contract used by Hub-managed AppServer processes.
/// </summary>
public static class ManagedAppServerEnvironment
{
    public const string ManagedFlag = "DOTCRAFT_MANAGED_APP_SERVER";
    public const string HubApiBaseUrl = "DOTCRAFT_HUB_API_BASE_URL";
    public const string HubToken = "DOTCRAFT_HUB_TOKEN";
    public const string WebSocketHost = "DOTCRAFT_MANAGED_APPSERVER_WS_HOST";
    public const string WebSocketPort = "DOTCRAFT_MANAGED_APPSERVER_WS_PORT";
    public const string WebSocketToken = "DOTCRAFT_MANAGED_APPSERVER_WS_TOKEN";
    public const string DashboardHost = "DOTCRAFT_MANAGED_DASHBOARD_HOST";
    public const string DashboardPort = "DOTCRAFT_MANAGED_DASHBOARD_PORT";
    public const string ApiHost = "DOTCRAFT_MANAGED_API_HOST";
    public const string ApiPort = "DOTCRAFT_MANAGED_API_PORT";
    public const string AguiHost = "DOTCRAFT_MANAGED_AGUI_HOST";
    public const string AguiPort = "DOTCRAFT_MANAGED_AGUI_PORT";
    public const string ProxyEndpoint = RuntimeConfigOverrides.ManagedProxyEndpoint;
    public const string ProxyApiKey = RuntimeConfigOverrides.ManagedProxyApiKey;

    /// <summary>
    /// Returns whether the current AppServer process was launched by Hub.
    /// </summary>
    public static bool IsManaged =>
        string.Equals(Environment.GetEnvironmentVariable(ManagedFlag), "1", StringComparison.Ordinal);

    /// <summary>
    /// Applies Hub-provided runtime overrides to in-memory configuration only.
    /// </summary>
    public static void ApplyTo(AppConfig config)
    {
        if (!IsManaged)
            return;

        var wsHost = GetString(WebSocketHost) ?? "127.0.0.1";
        var wsPort = GetPort(WebSocketPort) ?? 9100;
        var wsToken = GetString(WebSocketToken) ?? CreateToken();

        config.SetSection("AppServer", new AppServerConfig
        {
            Mode = AppServerMode.StdioAndWebSocket,
            WebSocket = new WebSocketServerConfig
            {
                Host = wsHost,
                Port = wsPort,
                Token = wsToken
            }
        });

        ApplyDashboard(config);
        ApplyModuleEndpoint(config, "Api", ApiHost, ApiPort);
        ApplyModuleEndpoint(config, "AgUi", AguiHost, AguiPort);
        RuntimeConfigOverrides.ApplyManagedProxy(config);
    }

    private static void ApplyDashboard(AppConfig config)
    {
        var host = GetString(DashboardHost);
        var port = GetPort(DashboardPort);
        if (host is null && port is null)
            return;

        if (host is not null)
            config.DashBoard.Host = host;
        if (port is not null)
            config.DashBoard.Port = port.Value;
    }

    private static void ApplyModuleEndpoint(AppConfig config, string sectionKey, string hostEnv, string portEnv)
    {
        var host = GetString(hostEnv);
        var port = GetPort(portEnv);
        if (host is null && port is null)
            return;

        var sectionType = FindConfigSectionType(sectionKey);
        if (sectionType is null)
            return;

        var getSection = typeof(AppConfig)
            .GetMethod(nameof(AppConfig.GetSection), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(sectionType);
        var setSection = typeof(AppConfig)
            .GetMethod(nameof(AppConfig.SetSection), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(sectionType);

        var section = getSection.Invoke(config, [sectionKey]);
        if (section is null)
            return;

        var hostProp = sectionType.GetProperty("Host", BindingFlags.Public | BindingFlags.Instance);
        if (hostProp is { CanWrite: true } && hostProp.PropertyType == typeof(string) && host is not null)
            hostProp.SetValue(section, host);

        var portProp = sectionType.GetProperty("Port", BindingFlags.Public | BindingFlags.Instance);
        if (portProp is { CanWrite: true } && portProp.PropertyType == typeof(int) && port is not null)
            portProp.SetValue(section, port.Value);

        setSection.Invoke(config, [sectionKey, section]);
    }

    internal static Type? FindConfigSectionType(string sectionKey)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<ConfigSectionAttribute>();
                if (attr != null && string.Equals(attr.Key, sectionKey, StringComparison.OrdinalIgnoreCase))
                    return type;
            }
        }

        return sectionKey.Equals("Api", StringComparison.OrdinalIgnoreCase)
            ? Type.GetType("DotCraft.Api.ApiConfig, DotCraft.Api", throwOnError: false)
            : sectionKey.Equals("AgUi", StringComparison.OrdinalIgnoreCase)
                ? Type.GetType("DotCraft.Agui.AguiConfig, DotCraft.Agui", throwOnError: false)
                : null;
    }

    private static string? GetString(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? GetPort(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!int.TryParse(value, out var port) || port <= 0 || port > 65535)
            return null;
        return port;
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
