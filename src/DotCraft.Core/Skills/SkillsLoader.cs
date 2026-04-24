using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DotCraft.Skills;

/// <summary>
/// Loader for agent skills from Skills/ directory.
/// Skills are markdown files (SKILL.md) that teach the agent specific capabilities.
/// </summary>
public sealed class SkillsLoader(string workspaceRoot)
{
    private HashSet<string> _disabledSkills = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the workspace skills path.
    /// </summary>
    public string WorkspaceSkillsPath { get; } = Path.Combine(workspaceRoot, "skills");

    /// <summary>
    /// Gets the user skills path.
    /// </summary>
    public string UserSkillsPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft", "skills");

    /// <summary>
    /// Replaces the set of disabled skill names (workspace UI / config). Disabled skills stay on disk but are omitted from agent context.
    /// </summary>
    public void SetDisabledSkills(IEnumerable<string>? names)
    {
        _disabledSkills = new HashSet<string>(names ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether a skill is enabled (not listed in disabled set).
    /// </summary>
    public bool IsSkillEnabled(string name) => !_disabledSkills.Contains(name);

    /// <summary>
    /// List all available skills (workspace and builtin).
    /// </summary>
    /// <param name="filterUnavailable">If true, filter out skills with unmet requirements.</param>
    public List<SkillInfo> ListSkills(bool filterUnavailable = true)
    {
        var skills = new List<SkillInfo>();

        // Workspace skills (highest priority)
        if (Directory.Exists(WorkspaceSkillsPath))
        {
            foreach (var dir in Directory.GetDirectories(WorkspaceSkillsPath))
            {
                var skillFile = Path.Combine(dir, "SKILL.md");
                if (File.Exists(skillFile))
                {
                    var name = Path.GetFileName(dir);
                    var metadata = GetSkillMetadata(name);
                    var requirements = GetSkillRequirements(metadata);

                    // Skills deployed by DeployBuiltInSkills() carry a .builtin marker
                    var isBuiltIn = File.Exists(Path.Combine(dir, ".builtin"));

                    var skillInfo = new SkillInfo
                    {
                        Name = name,
                        Path = skillFile,
                        Source = isBuiltIn ? "builtin" : "workspace",
                        Requirements = requirements
                    };

                    // Check availability
                    CheckRequirements(requirements, out var unavailableReason);
                    skillInfo.Available = string.IsNullOrEmpty(unavailableReason);
                    skillInfo.UnavailableReason = unavailableReason;
                    skillInfo.Enabled = !_disabledSkills.Contains(name);

                    skills.Add(skillInfo);
                }
            }
        }

        // User skills
        if (Directory.Exists(UserSkillsPath))
        {
            foreach (var dir in Directory.GetDirectories(UserSkillsPath))
            {
                var skillFile = Path.Combine(dir, "SKILL.md");
                if (File.Exists(skillFile))
                {
                    var name = Path.GetFileName(dir);
                    // Skip if workspace has skill with same name
                    if (skills.Any(s => s.Name == name))
                        continue;

                    var metadata = GetSkillMetadata(name);
                    var requirements = GetSkillRequirements(metadata);

                    var skillInfo = new SkillInfo
                    {
                        Name = name,
                        Path = skillFile,
                        Source = "user",
                        Requirements = requirements
                    };

                    // Check availability
                    CheckRequirements(requirements, out var unavailableReason);
                    skillInfo.Available = string.IsNullOrEmpty(unavailableReason);
                    skillInfo.UnavailableReason = unavailableReason;
                    skillInfo.Enabled = !_disabledSkills.Contains(name);

                    skills.Add(skillInfo);
                }
            }
        }

        // Filter by requirements if requested
        if (filterUnavailable)
            return skills.Where(s => s.Available).ToList();

        return skills;
    }

    /// <summary>
    /// Load a skill by name.
    /// </summary>
    public string? LoadSkill(string name)
    {
        // Check workspace first
        var workspaceSkill = Path.Combine(WorkspaceSkillsPath, name, "SKILL.md");
        if (File.Exists(workspaceSkill))
            return File.ReadAllText(workspaceSkill, Encoding.UTF8);

        // Check user-level skills directory
        var userSkill = Path.Combine(UserSkillsPath, name, "SKILL.md");
        if (File.Exists(userSkill))
            return File.ReadAllText(userSkill, Encoding.UTF8);

        return null;
    }

    /// <summary>
    /// Load specific skills for inclusion in agent context.
    /// </summary>
    public string LoadSkillsForContext(IEnumerable<string> skillNames)
    {
        var parts = new List<string>();

        foreach (var name in skillNames)
        {
            if (_disabledSkills.Contains(name))
                continue;
            var content = LoadSkill(name);
            if (content == null)
                continue;

            content = StripFrontmatter(content);
            parts.Add($"### Skill: {name}\n\n{content}");
        }

        return parts.Count > 0 ? string.Join("\n\n---\n\n", parts) : string.Empty;
    }

    /// <summary>
    /// Deploy built-in skills (embedded in the assembly) to the user skills directory.
    /// Skips skills that were created by the user (no .builtin marker) and skills
    /// that are already up to date.
    /// </summary>
    /// <param name="resourceAssembly">
    /// The assembly that contains the embedded skill resources.
    /// Pass <c>typeof(Program).Assembly</c> (or equivalent) from the host application.
    /// Falls back to the assembly containing <see cref="SkillsLoader"/> when null.
    /// </param>
    public void DeployBuiltInSkills(Assembly? resourceAssembly = null)
    {
        const string resourcePrefix = "DotCraft.Skills.BuiltIn.";
        const string markerFile = ".builtin";

        var assembly = resourceAssembly ?? typeof(SkillsLoader).Assembly;
        var currentVersion = assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        // Group resources by skill name
        var resourcesBySkill = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                var remainder = name[resourcePrefix.Length..];
                var dotIndex = remainder.IndexOf('.');
                // Resources at the BuiltIn root (e.g. .gitkeep) have no skill prefix
                if (dotIndex <= 0)
                    return (SkillName: string.Empty, FileName: remainder, ResourceName: name);
                return (
                    SkillName: remainder[..dotIndex],
                    FileName: remainder[(dotIndex + 1)..],
                    ResourceName: name
                );
            })
            .Where(r => !string.IsNullOrEmpty(r.SkillName))
            .GroupBy(r => r.SkillName);

        Directory.CreateDirectory(WorkspaceSkillsPath);

        foreach (var skillGroup in resourcesBySkill)
        {
            var skillName = skillGroup.Key;
            var skillDir = Path.Combine(WorkspaceSkillsPath, skillName);
            var markerPath = Path.Combine(skillDir, markerFile);

            // If the skill directory exists but has no .builtin marker, the user owns it
            if (Directory.Exists(skillDir) && !File.Exists(markerPath))
                continue;

            // If the skill is already at the current version, skip it
            if (File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == currentVersion)
                continue;

            Directory.CreateDirectory(skillDir);

            foreach (var resource in skillGroup)
            {
                using var stream = assembly.GetManifestResourceStream(resource.ResourceName);
                if (stream == null)
                    continue;

                var targetPath = Path.Combine(skillDir, resource.FileName);
                using var file = File.Create(targetPath);
                stream.CopyTo(file);
            }

            File.WriteAllText(markerPath, currentVersion);
        }
    }

    /// <summary>
    /// Build a summary of all skills (for progressive loading).
    /// The agent can read the full skill content using ReadFile when needed.
    /// Shows availability status and missing requirements for unavailable skills.
    /// </summary>
    public string BuildSkillsSummary(IReadOnlyCollection<string>? availableToolNames = null)
    {
        var allSkills = ListSkills(filterUnavailable: false);
        if (allSkills.Count == 0)
            return string.Empty;

        var toolSet = availableToolNames == null
            ? null
            : new HashSet<string>(availableToolNames, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("<skills>");

        foreach (var skill in allSkills)
        {
            if (!skill.Enabled)
                continue;

            var description = GetSkillDescription(skill.Name);
            var metadata = GetSkillMetadata(skill.Name);
            var alwaysLoad = metadata?.GetValueOrDefault("always", "false").ToLowerInvariant() == "true";
            var available = skill.Available;
            var unavailableReason = skill.UnavailableReason;
            if (toolSet != null && skill.Requirements?.Tools.Count > 0)
            {
                var missingTools = skill.Requirements.Tools
                    .Where(t => !toolSet.Contains(t))
                    .ToArray();
                if (missingTools.Length > 0)
                {
                    available = false;
                    unavailableReason = "Missing tools: " + string.Join(", ", missingTools);
                }
            }

            sb.AppendLine(
                $"  <skill available=\"{available.ToString().ToLower()}\" always=\"{alwaysLoad.ToString().ToLower()}\">");
            sb.AppendLine($"    <name>{EscapeXml(skill.Name)}</name>");
            sb.AppendLine($"    <description>{EscapeXml(description)}</description>");
            sb.AppendLine($"    <location>{skill.Path}</location>");

            // Show missing requirements for unavailable skills
            if (!available && unavailableReason != null)
            {
                sb.AppendLine($"    <requires>{EscapeXml(unavailableReason)}</requires>");
            }

            sb.AppendLine("  </skill>");
        }

        sb.AppendLine("</skills>");
        return sb.ToString();
    }

    /// <summary>
    /// Get skills marked as always=true.
    /// </summary>
    public List<string> GetAlwaysSkills()
    {
        var result = new List<string>();

        foreach (var skill in ListSkills())
        {
            if (!skill.Enabled)
                continue;
            var metadata = GetSkillMetadata(skill.Name);
            if (metadata != null && metadata.GetValueOrDefault("always", "false").ToLowerInvariant() == "true")
            {
                result.Add(skill.Name);
            }
        }

        return result;
    }

    /// <summary>
    /// Get skill metadata from frontmatter (simple YAML parsing).
    /// </summary>
    public Dictionary<string, string>? GetSkillMetadata(string name)
    {
        var content = LoadSkill(name);
        if (content == null || !content.StartsWith("---"))
            return null;

        var match = Regex.Match(content, @"^---\r?\n(.*?)\r?\n---", RegexOptions.Singleline);
        if (!match.Success)
            return null;

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in match.Groups[1].Value.Split('\n'))
        {
            if (!line.Contains(':'))
                continue;

            var parts = line.Split(':', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim().Trim('"', '\'');
                metadata[key] = value;
            }
        }

        return metadata;
    }

    /// <summary>
    /// Human-readable description from frontmatter, or the skill name.
    /// </summary>
    public string GetSkillDescription(string name)
    {
        var metadata = GetSkillMetadata(name);
        return metadata?.GetValueOrDefault("description", name) ?? name;
    }

    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---"))
            return content;

        var match = Regex.Match(content, @"^---\r?\n.*?\r?\n---\r?\n", RegexOptions.Singleline);
        return match.Success ? content[match.Length..].Trim() : content;
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    public sealed class SkillInfo
    {
        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Whether the skill is available (all requirements met).
        /// </summary>
        public bool Available { get; set; } = true;

        /// <summary>
        /// Reason why the skill is unavailable (if applicable).
        /// </summary>
        public string? UnavailableReason { get; set; }

        /// <summary>
        /// Skill requirements (bins, env vars).
        /// </summary>
        public SkillRequirements? Requirements { get; set; }

        /// <summary>
        /// When false, the skill is disabled via workspace config and omitted from agent context.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Represents skill requirements.
    /// </summary>
    public sealed class SkillRequirements
    {
        /// <summary>
        /// Required executables/bins.
        /// </summary>
        public List<string> Bins { get; set; } = [];

        /// <summary>
        /// Required environment variables.
        /// </summary>
        public List<string> Env { get; set; } = [];

        /// <summary>
        /// Required agent tools.
        /// </summary>
        public List<string> Tools { get; set; } = [];
    }

    /// <summary>
    /// Check if skill requirements are met (bins, env vars).
    /// </summary>
    /// <param name="requires">Requirements to check.</param>
    /// <param name="reason">Output parameter for missing requirements description.</param>
    /// <returns>True if all requirements are met, false otherwise.</returns>
    private static bool CheckRequirements(SkillRequirements? requires, out string? reason)
    {
        reason = null;
        if (requires == null || (requires.Bins.Count == 0 && requires.Env.Count == 0))
            return true;

        var missing = new List<string>();

        // Check required executables
        foreach (var bin in requires.Bins)
        {
            if (!IsCommandAvailable(bin))
                missing.Add($"Executable: {bin}");
        }

        // Check required environment variables
        foreach (var env in requires.Env)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env)))
                missing.Add($"Environment variable: {env}");
        }

        if (missing.Count > 0)
        {
            reason = "Missing requirements: " + string.Join(", ", missing);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Check if a command is available in PATH.
    /// </summary>
    /// <param name="command">The command to check.</param>
    /// <returns>True if command is available, false otherwise.</returns>
    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var checkCommand = isWindows ? $"where {command}" : $"which {command}";
            var shell = isWindows ? "cmd.exe" : "/bin/bash";
            var shellArg = isWindows ? "/c" : "-c";

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"{shellArg} \"{checkCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parse skill requirements from metadata.
    /// </summary>
    /// <param name="metadata">Skill metadata dictionary.</param>
    /// <returns>SkillRequirements object, or null if no requirements.</returns>
    private static SkillRequirements? GetSkillRequirements(Dictionary<string, string>? metadata)
    {
        if (metadata == null)
            return null;

        // Check for 'requires' field with bins and env
        var hasRequirements = metadata.ContainsKey("bins") || metadata.ContainsKey("env") || metadata.ContainsKey("tools");

        if (!hasRequirements)
            return null;

        var requirements = new SkillRequirements();

        // Parse bins (comma-separated)
        if (metadata.TryGetValue("bins", out var binsStr))
        {
            requirements.Bins.AddRange(binsStr.Split(',')
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrEmpty(b)));
        }

        // Parse env (comma-separated)
        if (metadata.TryGetValue("env", out var envStr))
        {
            requirements.Env.AddRange(envStr.Split(',')
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrEmpty(e)));
        }

        // Parse tools (comma-separated). Tool requirements are evaluated against
        // the per-agent tool list when building the prompt.
        if (metadata.TryGetValue("tools", out var toolsStr))
        {
            requirements.Tools.AddRange(toolsStr.Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t)));
        }

        return requirements.Bins.Count == 0 && requirements.Env.Count == 0 && requirements.Tools.Count == 0
            ? null
            : requirements;
    }
}
