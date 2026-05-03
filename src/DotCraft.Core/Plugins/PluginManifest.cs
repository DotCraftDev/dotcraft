using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DotCraft.Plugins;

/// <summary>
/// Parsed DotCraft plugin manifest.
/// </summary>
public sealed record PluginManifest
{
    public required int SchemaVersion { get; init; }

    public required string Id { get; init; }

    public string? Version { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyList<PluginManifestFunction> Functions { get; init; } = [];

    public IReadOnlyDictionary<string, PluginManifestProcess> Processes { get; init; } =
        new Dictionary<string, PluginManifestProcess>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Paths { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("interface")]
    public PluginInterfaceMetadata? Interface { get; init; }

    public string? SkillsPath { get; init; }

    public required string RootPath { get; init; }

    public required string ManifestPath { get; init; }
}

/// <summary>
/// Function declaration inside a DotCraft plugin manifest.
/// </summary>
public sealed record PluginManifestFunction
{
    public string? Namespace { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public JsonObject? InputSchema { get; init; }

    public JsonObject? OutputSchema { get; init; }

    public PluginFunctionDisplay? Display { get; init; }

    public PluginFunctionApprovalDescriptor? Approval { get; init; }

    public bool RequiresChatContext { get; init; }

    public bool? DeferLoading { get; init; }

    public required PluginManifestBackend Backend { get; init; }

    public PluginFunctionDescriptor ToDescriptor(string pluginId) =>
        new()
        {
            PluginId = pluginId,
            Namespace = Namespace,
            Name = Name,
            Description = Description,
            InputSchema = InputSchema?.DeepClone() as JsonObject,
            OutputSchema = OutputSchema?.DeepClone() as JsonObject,
            Display = Display,
            Approval = Approval,
            RequiresChatContext = RequiresChatContext,
            DeferLoading = DeferLoading
        };
}

/// <summary>
/// Backend declaration inside a DotCraft plugin manifest function.
/// </summary>
public sealed record PluginManifestBackend
{
    public required string Kind { get; init; }

    public string? ProviderId { get; init; }

    public string? FunctionName { get; init; }

    public string? ProcessId { get; init; }

    public string? ToolName { get; init; }
}

/// <summary>
/// External process declaration for plugin dynamic tools.
/// </summary>
public sealed record PluginManifestProcess
{
    public required string Id { get; init; }

    public required string Command { get; init; }

    public IReadOnlyList<string> Args { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();

    public double? StartupTimeoutSeconds { get; init; }

    public double? ToolTimeoutSeconds { get; init; }
}

public sealed record PluginInterfaceMetadata
{
    public string? DisplayName { get; init; }

    public string? ShortDescription { get; init; }

    public string? LongDescription { get; init; }

    public string? DeveloperName { get; init; }

    public string? Category { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public string? DefaultPrompt { get; init; }

    public string? BrandColor { get; init; }

    public string? ComposerIcon { get; init; }

    public string? Logo { get; init; }

    public string? WebsiteUrl { get; init; }

    public string? PrivacyPolicyUrl { get; init; }

    public string? TermsOfServiceUrl { get; init; }
}

/// <summary>
/// Result of parsing one DotCraft plugin manifest.
/// </summary>
public sealed record PluginManifestParseResult(
    PluginManifest? Manifest,
    IReadOnlyList<PluginDiagnostic> Diagnostics);

/// <summary>
/// Parser for <c>.craft-plugin/plugin.json</c>.
/// </summary>
public static partial class PluginManifestParser
{
    public const int SupportedSchemaVersion = 1;
    public const string ManifestRelativePath = ".craft-plugin/plugin.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Loads and validates a plugin manifest from a plugin root.
    /// </summary>
    public static PluginManifestParseResult Load(string pluginRoot)
    {
        var manifestPath = Path.Combine(pluginRoot, ".craft-plugin", "plugin.json");
        var diagnostics = new List<PluginDiagnostic>();
        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "PluginManifestMissing",
                "Plugin manifest is missing.",
                path: manifestPath));
            return new PluginManifestParseResult(null, diagnostics);
        }

        RawPluginManifest? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawPluginManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginManifestJson",
                $"Failed to parse plugin manifest JSON: {ex.Message}",
                path: manifestPath));
            return new PluginManifestParseResult(null, diagnostics);
        }
        catch (IOException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginManifestReadFailed",
                $"Failed to read plugin manifest: {ex.Message}",
                path: manifestPath));
            return new PluginManifestParseResult(null, diagnostics);
        }

        if (raw == null)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginManifest",
                "Plugin manifest is empty.",
                path: manifestPath));
            return new PluginManifestParseResult(null, diagnostics);
        }

        if (raw.SchemaVersion != SupportedSchemaVersion)
            diagnostics.Add(PluginDiagnostic.Error(
                "UnsupportedPluginManifestVersion",
                $"Unsupported plugin manifest schemaVersion '{raw.SchemaVersion}'.",
                raw.Id,
                path: manifestPath));

        if (!IsValidPluginId(raw.Id))
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginId",
                "Plugin id is required and may contain only ASCII letters, digits, '.', '_', '-', or ':'.",
                raw.Id,
                path: manifestPath));

        if (string.IsNullOrWhiteSpace(raw.DisplayName))
            diagnostics.Add(PluginDiagnostic.Error(
                "MissingPluginDisplayName",
                "Plugin displayName is required.",
                raw.Id,
                path: manifestPath));

        var resolvedPaths = ResolvePaths(pluginRoot, raw.Paths, raw.Id, manifestPath, diagnostics);
        var skillsPath = ResolveOptionalManifestPath(
            pluginRoot,
            raw.Skills,
            "skills",
            raw.Id,
            manifestPath,
            diagnostics);
        var interfaceMetadata = ParseInterface(
            pluginRoot,
            raw.Interface,
            raw.Id,
            manifestPath,
            diagnostics);
        var processes = ParseProcesses(pluginRoot, raw, manifestPath, diagnostics);
        var functions = ParseFunctions(raw, manifestPath, diagnostics);
        if (skillsPath == null && functions.Count == 0)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "MissingPluginCapabilities",
                "Plugin manifest must declare a skills path or at least one tool.",
                raw.Id,
                path: manifestPath));
        }

        if (diagnostics.Any(d => d.Severity == PluginDiagnosticSeverity.Error))
            return new PluginManifestParseResult(null, diagnostics);

        var manifest = new PluginManifest
        {
            SchemaVersion = raw.SchemaVersion,
            Id = raw.Id!.Trim(),
            Version = NormalizeOptional(raw.Version),
            DisplayName = raw.DisplayName!.Trim(),
            Description = NormalizeOptional(raw.Description),
            Capabilities = raw.Capabilities
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Select(capability => capability.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Functions = functions,
            Processes = processes,
            Paths = resolvedPaths,
            Interface = interfaceMetadata,
            SkillsPath = skillsPath,
            RootPath = Path.GetFullPath(pluginRoot),
            ManifestPath = Path.GetFullPath(manifestPath)
        };

        return new PluginManifestParseResult(manifest, diagnostics);
    }

    /// <summary>
    /// Returns whether the directory contains a DotCraft plugin manifest.
    /// </summary>
    public static bool IsValidPluginRoot(string path) =>
        File.Exists(Path.Combine(path, ".craft-plugin", "plugin.json"));

    /// <summary>
    /// Returns whether a string is a valid plugin id.
    /// </summary>
    public static bool IsValidPluginId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && PluginIdRegex().IsMatch(value);

    /// <summary>
    /// Returns whether a string is a valid model-visible function name.
    /// </summary>
    public static bool IsValidFunctionName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && FunctionNameRegex().IsMatch(value);

    /// <summary>
    /// Returns whether a string is a valid plugin process id.
    /// </summary>
    public static bool IsValidProcessId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ProcessIdRegex().IsMatch(value);

    private static IReadOnlyList<PluginManifestFunction> ParseFunctions(
        RawPluginManifest raw,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        var rawFunctions = new List<RawPluginFunction>();
        if (raw.Functions is { Count: > 0 })
            rawFunctions.AddRange(raw.Functions);
        if (raw.Tools is { Count: > 0 })
            rawFunctions.AddRange(raw.Tools);

        if (rawFunctions.Count == 0)
            return [];

        var functions = new List<PluginManifestFunction>();
        foreach (var function in rawFunctions)
        {
            if (!IsValidFunctionName(function.Name))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginFunctionName",
                    "Plugin function name is required and must be a valid model-visible function name.",
                    raw.Id,
                    function.Name,
                    manifestPath));
                continue;
            }

            if (string.IsNullOrWhiteSpace(function.Description))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "MissingPluginFunctionDescription",
                    $"Plugin function '{function.Name}' must declare a description.",
                    raw.Id,
                    function.Name,
                    manifestPath));
                continue;
            }

            var inputSchema = function.InputSchema ?? new JsonObject { ["type"] = "object" };
            if (!PluginFunctionSchemaValidator.TryValidateSchema(inputSchema, out var schemaError))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginFunctionInputSchema",
                    $"Plugin function '{function.Name}' has an invalid inputSchema: {schemaError}",
                    raw.Id,
                    function.Name,
                    manifestPath));
                continue;
            }

            if (function.OutputSchema != null
                && !PluginFunctionSchemaValidator.TryValidateSchema(function.OutputSchema, out schemaError))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginFunctionOutputSchema",
                    $"Plugin function '{function.Name}' has an invalid outputSchema: {schemaError}",
                    raw.Id,
                    function.Name,
                    manifestPath));
                continue;
            }

            if (function.Backend == null || string.IsNullOrWhiteSpace(function.Backend.Kind))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "MissingPluginFunctionBackend",
                    $"Plugin function '{function.Name}' must declare a backend.",
                    raw.Id,
                    function.Name,
                    manifestPath));
                continue;
            }

            functions.Add(new PluginManifestFunction
            {
                Namespace = NormalizeOptional(function.Namespace),
                Name = function.Name!.Trim(),
                Description = function.Description!.Trim(),
                InputSchema = inputSchema.DeepClone() as JsonObject,
                OutputSchema = function.OutputSchema?.DeepClone() as JsonObject,
                Display = function.Display,
                Approval = function.Approval,
                RequiresChatContext = function.RequiresChatContext,
                DeferLoading = function.DeferLoading,
                Backend = new PluginManifestBackend
                {
                    Kind = function.Backend.Kind.Trim(),
                    ProviderId = NormalizeOptional(function.Backend.ProviderId),
                    FunctionName = NormalizeOptional(function.Backend.FunctionName),
                    ProcessId = NormalizeOptional(function.Backend.ProcessId),
                    ToolName = NormalizeOptional(function.Backend.ToolName)
                }
            });
        }

        return functions;
    }

    private static IReadOnlyDictionary<string, PluginManifestProcess> ParseProcesses(
        string pluginRoot,
        RawPluginManifest raw,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (raw.Processes == null || raw.Processes.Count == 0)
            return new Dictionary<string, PluginManifestProcess>(StringComparer.OrdinalIgnoreCase);

        var processes = new Dictionary<string, PluginManifestProcess>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawId, process) in raw.Processes)
        {
            var id = NormalizeOptional(rawId);
            if (!IsValidProcessId(id))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginProcessId",
                    "Plugin process id is required and may contain only ASCII letters, digits, '.', '_', or '-'.",
                    raw.Id,
                    path: manifestPath));
                continue;
            }

            if (process == null || string.IsNullOrWhiteSpace(process.Command))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginProcess",
                    $"Plugin process '{id}' must declare a command.",
                    raw.Id,
                    path: manifestPath));
                continue;
            }

            if (process.WorkingDirectory != null
                && ResolveManifestPath(pluginRoot, process.WorkingDirectory, out var workingDirectoryError) == null)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginProcessWorkingDirectory",
                    $"Plugin process '{id}' has invalid workingDirectory: {workingDirectoryError}",
                    raw.Id,
                    path: manifestPath));
                continue;
            }

            if (process.Command.StartsWith("./", StringComparison.Ordinal)
                && ResolveManifestPath(pluginRoot, process.Command, out var commandError) == null)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginProcessCommand",
                    $"Plugin process '{id}' has invalid command path: {commandError}",
                    raw.Id,
                    path: manifestPath));
                continue;
            }

            var args = process.Args
                .Where(arg => !string.IsNullOrWhiteSpace(arg))
                .Select(arg => arg.Trim())
                .ToArray();
            var invalidArg = args.FirstOrDefault(arg =>
                arg.StartsWith("./", StringComparison.Ordinal)
                && ResolveManifestPath(pluginRoot, arg, out _) == null);
            if (invalidArg != null)
            {
                _ = ResolveManifestPath(pluginRoot, invalidArg, out var argError);
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginProcessArgument",
                    $"Plugin process '{id}' has invalid manifest-relative argument '{invalidArg}': {argError}",
                    raw.Id,
                    path: manifestPath));
                continue;
            }

            processes[id!] = new PluginManifestProcess
            {
                Id = id!,
                Command = process.Command.Trim(),
                Args = args,
                WorkingDirectory = NormalizeOptional(process.WorkingDirectory),
                Env = process.Env ?? new Dictionary<string, string>(StringComparer.Ordinal),
                StartupTimeoutSeconds = process.StartupTimeoutSeconds,
                ToolTimeoutSeconds = process.ToolTimeoutSeconds
            };
        }

        return processes;
    }

    private static IReadOnlyDictionary<string, string> ResolvePaths(
        string pluginRoot,
        Dictionary<string, string>? paths,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (paths == null)
            return resolved;

        foreach (var (name, value) in paths)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var normalized = ResolveManifestPath(pluginRoot, value, out var error);
            if (normalized == null)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginManifestPath",
                    $"Manifest path '{name}' is invalid: {error}",
                    pluginId,
                    path: manifestPath));
                continue;
            }

            resolved[name] = normalized;
        }

        return resolved;
    }

    private static string? ResolveOptionalManifestPath(
        string pluginRoot,
        string? value,
        string fieldName,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = ResolveManifestPath(pluginRoot, value, out var error);
        if (normalized != null)
            return normalized;

        diagnostics.Add(PluginDiagnostic.Error(
            "InvalidPluginManifestPath",
            $"Manifest path '{fieldName}' is invalid: {error}",
            pluginId,
            path: manifestPath));
        return null;
    }

    private static PluginInterfaceMetadata? ParseInterface(
        string pluginRoot,
        RawPluginInterface? raw,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (raw == null)
            return null;

        var composerIcon = ResolveOptionalManifestPath(
            pluginRoot,
            raw.ComposerIcon,
            "interface.composerIcon",
            pluginId,
            manifestPath,
            diagnostics);
        var logo = ResolveOptionalManifestPath(
            pluginRoot,
            raw.Logo,
            "interface.logo",
            pluginId,
            manifestPath,
            diagnostics);

        return new PluginInterfaceMetadata
        {
            DisplayName = NormalizeOptional(raw.DisplayName),
            ShortDescription = NormalizeOptional(raw.ShortDescription),
            LongDescription = NormalizeOptional(raw.LongDescription),
            DeveloperName = NormalizeOptional(raw.DeveloperName),
            Category = NormalizeOptional(raw.Category),
            Capabilities = raw.Capabilities
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Select(capability => capability.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DefaultPrompt = NormalizeOptional(raw.DefaultPrompt),
            BrandColor = NormalizeOptional(raw.BrandColor),
            ComposerIcon = composerIcon,
            Logo = logo,
            WebsiteUrl = NormalizeOptional(raw.WebsiteUrl),
            PrivacyPolicyUrl = NormalizeOptional(raw.PrivacyPolicyUrl),
            TermsOfServiceUrl = NormalizeOptional(raw.TermsOfServiceUrl)
        };
    }

    private static string? ResolveManifestPath(string pluginRoot, string value, out string error)
    {
        error = string.Empty;
        if (!value.StartsWith("./", StringComparison.Ordinal))
        {
            error = "path must start with './'";
            return null;
        }

        var relative = value[2..];
        if (string.IsNullOrWhiteSpace(relative))
            return Path.GetFullPath(pluginRoot);

        if (relative
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == ".."))
        {
            error = "path must not contain '..'";
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(pluginRoot, relative));
        var root = Path.GetFullPath(pluginRoot);
        var relativeBack = Path.GetRelativePath(root, candidate);
        if (relativeBack.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativeBack))
        {
            error = "path must stay within the plugin root";
            return null;
        }

        return candidate;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$")]
    private static partial Regex PluginIdRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex FunctionNameRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex ProcessIdRegex();

    private sealed class RawPluginManifest
    {
        public int SchemaVersion { get; set; }

        public string? Id { get; set; }

        public string? Version { get; set; }

        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        public List<string> Capabilities { get; set; } = [];

        public Dictionary<string, string>? Paths { get; set; }

        public string? Skills { get; set; }

        [JsonPropertyName("interface")]
        public RawPluginInterface? Interface { get; set; }

        public List<RawPluginFunction>? Functions { get; set; }

        public List<RawPluginFunction>? Tools { get; set; }

        public Dictionary<string, RawPluginProcess>? Processes { get; set; }
    }

    private sealed class RawPluginInterface
    {
        public string? DisplayName { get; set; }

        public string? ShortDescription { get; set; }

        public string? LongDescription { get; set; }

        public string? DeveloperName { get; set; }

        public string? Category { get; set; }

        public List<string> Capabilities { get; set; } = [];

        public string? DefaultPrompt { get; set; }

        public string? BrandColor { get; set; }

        public string? ComposerIcon { get; set; }

        public string? Logo { get; set; }

        public string? WebsiteUrl { get; set; }

        public string? PrivacyPolicyUrl { get; set; }

        public string? TermsOfServiceUrl { get; set; }
    }

    private sealed class RawPluginFunction
    {
        public string? Namespace { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public JsonObject? InputSchema { get; set; }

        public JsonObject? OutputSchema { get; set; }

        public PluginFunctionDisplay? Display { get; set; }

        public PluginFunctionApprovalDescriptor? Approval { get; set; }

        public bool RequiresChatContext { get; set; }

        public bool? DeferLoading { get; set; }

        public RawPluginBackend? Backend { get; set; }
    }

    private sealed class RawPluginProcess
    {
        public string? Command { get; set; }

        public List<string> Args { get; set; } = [];

        public string? WorkingDirectory { get; set; }

        public Dictionary<string, string>? Env { get; set; }

        public double? StartupTimeoutSeconds { get; set; }

        public double? ToolTimeoutSeconds { get; set; }
    }

    private sealed class RawPluginBackend
    {
        public string? Kind { get; set; }

        public string? ProviderId { get; set; }

        public string? FunctionName { get; set; }

        public string? ProcessId { get; set; }

        public string? ToolName { get; set; }
    }
}
