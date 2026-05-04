using DotCraft.Acp;
using DotCraft.AppServer;
using DotCraft.Configuration;

namespace DotCraft.CLI;

/// <summary>
/// Lightweight, zero-dependency command-line argument parser for DotCraft.
///
/// <para>Supported usage forms:</para>
/// <list type="bullet">
/// <item><c>dotcraft exec "prompt"</c> — one-shot command-line agent run</item>
/// <item><c>dotcraft exec -</c> — one-shot run with the prompt read from stdin</item>
/// <item><c>dotcraft exec --remote ws://host:port/ws [--token T] "prompt"</c> — one-shot run connected to a remote AppServer</item>
/// <item><c>dotcraft app-server</c> — AppServer in stdio mode (backward-compatible)</item>
/// <item><c>dotcraft app-server --listen ws://host:port</c> — AppServer in pure WebSocket mode</item>
/// <item><c>dotcraft app-server --listen ws+stdio://host:port</c> — AppServer in stdio + WebSocket mode</item>
/// <item><c>dotcraft hub</c> — workspace-independent local Hub process</item>
/// <item><c>dotcraft -acp</c> / <c>dotcraft acp</c> — ACP bridge (stdio to IDE; AppServer subprocess or <c>--remote</c>)</item>
/// </list>
///
/// <para>
/// The <c>--listen</c> URL scheme determines the AppServer transport:
/// <c>stdio://</c> (default), <c>ws://</c> (pure WebSocket), or <c>ws+stdio://</c> (both).
/// Host and port are embedded in the URL, avoiding separate flags.
/// </para>
/// </summary>
public sealed record CommandLineArgs
{
    /// <summary>
    /// The top-level execution mode determined from the command-line arguments.
    /// </summary>
    public enum RunMode
    {
        /// <summary>No executable mode was supplied.</summary>
        None,

        /// <summary>One-shot command-line agent run.</summary>
        Exec,

        /// <summary>AppServer subprocess mode (wire protocol server).</summary>
        AppServer,

        /// <summary>Gateway host for concurrent channels and automations.</summary>
        Gateway,

        /// <summary>ACP (Agent Communication Protocol) mode for IDE integration.</summary>
        Acp,

        /// <summary>One-shot non-interactive workspace setup.</summary>
        Setup,

        /// <summary>Workspace-independent local Hub process.</summary>
        Hub,

        /// <summary>Non-interactive skill verification and installation commands.</summary>
        Skill
    }

    /// <summary>Top-level execution mode.</summary>
    public RunMode Mode { get; init; }

    /// <summary>
    /// <c>--listen</c> URL for <see cref="RunMode.AppServer"/> mode.
    /// <para>Examples: <c>stdio://</c>, <c>ws://127.0.0.1:9100</c>, <c>ws+stdio://127.0.0.1:9100</c>.</para>
    /// Null when not in AppServer mode or not specified (defaults to <c>stdio://</c>).
    /// </summary>
    public string? ListenUrl { get; init; }

    /// <summary>
    /// <c>--remote</c> URL for <see cref="RunMode.Exec"/> mode.
    /// When set, the CLI connects to an already-running AppServer via WebSocket
    /// instead of spawning a subprocess.
    /// <para>Example: <c>ws://127.0.0.1:9100/ws</c></para>
    /// </summary>
    public string? RemoteUrl { get; init; }

    /// <summary>
    /// <c>--token</c> for WebSocket authentication (both server-side and client-side).
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// Prompt text for <see cref="RunMode.Exec"/>. Null when stdin should be used or no prompt was supplied.
    /// </summary>
    public string? ExecPrompt { get; init; }

    /// <summary>
    /// Whether <see cref="RunMode.Exec"/> should read its prompt from stdin.
    /// </summary>
    public bool ExecReadStdin { get; init; }

    public string? SetupLanguage { get; init; }

    public string? SetupModel { get; init; }

    public string? SetupEndPoint { get; init; }

    public string? SetupApiKey { get; init; }

    public string? SetupProfile { get; init; }

    public bool SaveUserConfig { get; init; }

    public bool PreferExistingUserConfig { get; init; }

    public string? SkillCommand { get; init; }

    public string? SkillCandidatePath { get; init; }

    public string? SkillName { get; init; }

    public string? SkillSource { get; init; }

    public bool SkillOverwrite { get; init; }

    public bool SkillJson { get; init; }

    /// <summary>
    /// Whether this execution mode reserves stdout for a wire protocol (stdio-based JSON-RPC).
    /// When <c>true</c>, all console diagnostics must be redirected to stderr.
    /// </summary>
    public bool ReservesStdout { get; init; }

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parse raw command-line arguments into a <see cref="CommandLineArgs"/> instance.
    /// </summary>
    public static CommandLineArgs Parse(string[] args)
    {
        RunMode mode = RunMode.None;
        string? listenUrl = null;
        string? remoteUrl = null;
        string? token = null;
        string? execPrompt = null;
        var execReadStdin = false;
        var execPromptParts = new List<string>();
        string? setupLanguage = null;
        string? setupModel = null;
        string? setupEndPoint = null;
        string? setupApiKey = null;
        string? setupProfile = null;
        var saveUserConfig = false;
        var preferExistingUserConfig = false;
        string? skillCommand = null;
        string? skillCandidatePath = null;
        string? skillName = null;
        string? skillSource = null;
        var skillOverwrite = false;
        var skillJson = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("exec", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Exec;
                continue;
            }

            // Sub-command: app-server
            if (arg.Equals("app-server", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.AppServer;
                continue;
            }

            if (arg.Equals("gateway", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Gateway;
                continue;
            }

            if (arg.Equals("hub", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Hub;
                continue;
            }

            if (arg.Equals("skill", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Skill;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    skillCommand = args[++i];
                continue;
            }

            if (arg.Equals("setup", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Setup;
                continue;
            }

            // Sub-command: acp / -acp
            if (arg.Equals("acp", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-acp", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Acp;
                continue;
            }

            // --listen <URL>  (app-server transport)
            if (arg.Equals("--listen", StringComparison.OrdinalIgnoreCase))
            {
                listenUrl = ConsumeNext(args, ref i, "--listen");
                continue;
            }

            // --remote <URL>  (CLI connects to remote AppServer)
            if (arg.Equals("--remote", StringComparison.OrdinalIgnoreCase))
            {
                remoteUrl = ConsumeNext(args, ref i, "--remote");
                continue;
            }

            // --token <VALUE>
            if (arg.Equals("--token", StringComparison.OrdinalIgnoreCase))
            {
                token = ConsumeNext(args, ref i, "--token");
                continue;
            }

            if (arg.Equals("--language", StringComparison.OrdinalIgnoreCase))
            {
                setupLanguage = ConsumeNext(args, ref i, "--language");
                continue;
            }

            if (arg.Equals("--model", StringComparison.OrdinalIgnoreCase))
            {
                setupModel = ConsumeNext(args, ref i, "--model");
                continue;
            }

            if (arg.Equals("--endpoint", StringComparison.OrdinalIgnoreCase))
            {
                setupEndPoint = ConsumeNext(args, ref i, "--endpoint");
                continue;
            }

            if (arg.Equals("--api-key", StringComparison.OrdinalIgnoreCase))
            {
                setupApiKey = ConsumeNext(args, ref i, "--api-key");
                continue;
            }

            if (arg.Equals("--profile", StringComparison.OrdinalIgnoreCase))
            {
                setupProfile = ConsumeNext(args, ref i, "--profile");
                continue;
            }

            if (arg.Equals("--save-user-config", StringComparison.OrdinalIgnoreCase))
            {
                saveUserConfig = true;
                continue;
            }

            if (arg.Equals("--prefer-existing-user-config", StringComparison.OrdinalIgnoreCase))
            {
                preferExistingUserConfig = true;
                continue;
            }

            if (arg.Equals("--candidate", StringComparison.OrdinalIgnoreCase))
            {
                skillCandidatePath = ConsumeNext(args, ref i, "--candidate");
                continue;
            }

            if (arg.Equals("--name", StringComparison.OrdinalIgnoreCase))
            {
                skillName = ConsumeNext(args, ref i, "--name");
                continue;
            }

            if (arg.Equals("--source", StringComparison.OrdinalIgnoreCase))
            {
                skillSource = ConsumeNext(args, ref i, "--source");
                continue;
            }

            if (arg.Equals("--overwrite", StringComparison.OrdinalIgnoreCase))
            {
                skillOverwrite = true;
                continue;
            }

            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                skillJson = true;
                continue;
            }

            // Support --listen=<url> / --remote=<url> / --token=<value> forms
            if (TryParseKeyValue(arg, "--listen", out var listenValue))
            {
                listenUrl = listenValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--remote", out var remoteValue))
            {
                remoteUrl = remoteValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--token", out var tokenValue))
            {
                token = tokenValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--language", out var languageValue))
            {
                setupLanguage = languageValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--model", out var modelValue))
            {
                setupModel = modelValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--endpoint", out var endpointValue))
            {
                setupEndPoint = endpointValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--api-key", out var apiKeyValue))
            {
                setupApiKey = apiKeyValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--profile", out var profileValue))
            {
                setupProfile = profileValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--candidate", out var candidateValue))
            {
                skillCandidatePath = candidateValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--name", out var nameValue))
            {
                skillName = nameValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--source", out var sourceValue))
            {
                skillSource = sourceValue;
                continue;
            }

            if (mode == RunMode.Exec)
            {
                execPromptParts.Add(arg);
            }
            // Unknown arguments outside exec are silently ignored (forward-compatible).
        }

        if (mode == RunMode.Exec)
        {
            execPrompt = string.Join(" ", execPromptParts).Trim();
            if (execPrompt == "-")
            {
                execPrompt = null;
                execReadStdin = true;
            }
        }

        // Determine whether stdout is reserved for a wire protocol.
        // - ACP always uses stdio JSON-RPC.
        // - AppServer uses stdio unless the listen URL is pure WebSocket (ws://).
        var reservesStdout = mode switch
        {
            RunMode.Acp => true,
            RunMode.AppServer => !IsPureWebSocketListen(listenUrl),
            RunMode.Exec => true,
            _ => false
        };

        return new CommandLineArgs
        {
            Mode = mode,
            ListenUrl = listenUrl,
            RemoteUrl = remoteUrl,
            Token = token,
            ExecPrompt = execPrompt,
            ExecReadStdin = execReadStdin,
            SetupLanguage = setupLanguage,
            SetupModel = setupModel,
            SetupEndPoint = setupEndPoint,
            SetupApiKey = setupApiKey,
            SetupProfile = setupProfile,
            SaveUserConfig = saveUserConfig,
            PreferExistingUserConfig = preferExistingUserConfig,
            SkillCommand = skillCommand,
            SkillCandidatePath = skillCandidatePath,
            SkillName = skillName,
            SkillSource = skillSource,
            SkillOverwrite = skillOverwrite,
            SkillJson = skillJson,
            ReservesStdout = reservesStdout
        };
    }

    // -------------------------------------------------------------------------
    // Config application
    // -------------------------------------------------------------------------

    /// <summary>
    /// Apply parsed CLI overrides onto the loaded <see cref="AppConfig"/>.
    /// Command-line arguments take precedence over config.json values.
    /// </summary>
    public void ApplyTo(AppConfig config)
    {
        switch (Mode)
        {
            case RunMode.Acp:
            {
                var acp = new AcpConfig { Enabled = true };
                if (!string.IsNullOrWhiteSpace(RemoteUrl))
                {
                    acp.AppServerUrl = RemoteUrl;
                    acp.AppServerToken = Token;
                }

                config.SetSection("Acp", acp);
                config.DashBoard.Enabled = false;
                break;
            }

            case RunMode.AppServer:
                ApplyAppServerConfig(config);
                break;

            case RunMode.Gateway:
                break;

            case RunMode.Exec:
                ApplyCliConfig(config);
                break;

            case RunMode.None:
            case RunMode.Setup:
                break;

            case RunMode.Hub:
            case RunMode.Skill:
                break;
        }
    }

    private void ApplyAppServerConfig(AppConfig config)
    {
        var (appServerMode, wsHost, wsPort) = ParseListenUrl(ListenUrl);

        var appServerConfig = new AppServerConfig { Mode = appServerMode };

        // Apply WebSocket settings when the mode includes WebSocket
        if (appServerMode is AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket)
        {
            appServerConfig.WebSocket = new WebSocketServerConfig
            {
                Host = wsHost ?? "127.0.0.1",
                Port = wsPort ?? 9100,
                Token = Token
            };
        }

        config.SetSection("AppServer", appServerConfig);
    }

    private void ApplyCliConfig(AppConfig config)
    {
        if (RemoteUrl is null)
            return;

        // --remote overrides CliConfig.AppServerUrl
        var cliConfig = config.GetSection<CliConfig>("CLI");
        cliConfig.AppServerUrl = RemoteUrl;

        if (Token is not null)
            cliConfig.AppServerToken = Token;

        config.SetSection("CLI", cliConfig);
    }

    // -------------------------------------------------------------------------
    // URL parsing helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parse a <c>--listen</c> URL into an <see cref="AppServerMode"/> and optional host/port.
    /// </summary>
    internal static (AppServerMode Mode, string? Host, int? Port) ParseListenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (AppServerMode.Stdio, null, null);

        // stdio:// → pure stdio
        if (url.StartsWith("stdio://", StringComparison.OrdinalIgnoreCase))
            return (AppServerMode.Stdio, null, null);

        // ws+stdio://host:port → stdio + WebSocket
        if (url.StartsWith("ws+stdio://", StringComparison.OrdinalIgnoreCase))
        {
            var (host, port) = ParseHostPort(url["ws+stdio://".Length..]);
            return (AppServerMode.StdioAndWebSocket, host, port);
        }

        // ws://host:port → pure WebSocket
        if (url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            var (host, port) = ParseHostPort(url["ws://".Length..]);
            return (AppServerMode.WebSocket, host, port);
        }

        // wss://host:port is not supported by the embedded listener yet.
        // Reject explicitly instead of silently downgrading to plain ws/http.
        if (url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The wss:// scheme is not currently supported. Use ws:// or terminate TLS in front of AppServer.");

        // Unrecognized scheme — treat as stdio with a warning
        Console.Error.WriteLine($"[CLI] Warning: unrecognized --listen URL scheme '{url}', defaulting to stdio.");
        return (AppServerMode.Stdio, null, null);
    }

    private static (string Host, int? Port) ParseHostPort(string hostPort)
    {
        // Remove trailing path (e.g., /ws)
        var pathIndex = hostPort.IndexOf('/');
        if (pathIndex >= 0)
            hostPort = hostPort[..pathIndex];

        var colonIndex = hostPort.LastIndexOf(':');
        if (colonIndex < 0)
            return (hostPort, null);

        var host = hostPort[..colonIndex];
        if (int.TryParse(hostPort[(colonIndex + 1)..], out var port))
            return (host, port);

        return (hostPort, null);
    }

    /// <summary>
    /// Returns <c>true</c> when the listen URL uses pure WebSocket mode (ws:// or wss://)
    /// and does NOT include stdio.
    /// </summary>
    private static bool IsPureWebSocketListen(string? listenUrl)
    {
        if (string.IsNullOrWhiteSpace(listenUrl))
            return false;

        // ws+stdio:// includes stdio, so it reserves stdout
        if (listenUrl.StartsWith("ws+stdio://", StringComparison.OrdinalIgnoreCase))
            return false;

        return listenUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
               listenUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Generic argument parsing helpers
    // -------------------------------------------------------------------------

    private static string ConsumeNext(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value after '{flag}'.");
        return args[++index];
    }

    private static bool TryParseKeyValue(string arg, string key, out string value)
    {
        var prefix = key + "=";
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg[prefix.Length..];
            return true;
        }

        value = default!;
        return false;
    }
}
