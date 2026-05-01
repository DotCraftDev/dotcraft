using System.Text;
using System.Text.Json;

namespace DotCraft.Skills;

/// <summary>
/// Verifies candidate skill bundles and publishes them into the workspace skill source root.
/// </summary>
public sealed class SkillInstallService(SkillsLoader skillsLoader)
{
    private const int MaxFileCount = 1000;
    private const long MaxTotalBytes = 100L * 1024 * 1024;
    private const string InstallMarkerFileName = ".dotcraft-skill.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Verifies that a candidate directory is a valid DotCraft skill bundle.
    /// </summary>
    public async Task<SkillInstallVerificationResult> VerifyAsync(
        SkillInstallVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var candidatePath = Path.GetFullPath(request.CandidatePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(request.CandidatePath) || !Directory.Exists(candidatePath))
        {
            errors.Add($"Candidate directory not found: {request.CandidatePath}");
            return SkillInstallVerificationResult.Invalid(candidatePath, request.ExpectedName, errors);
        }

        var skillFile = Path.Combine(candidatePath, "SKILL.md");
        if (!File.Exists(skillFile))
        {
            errors.Add("Candidate directory must contain SKILL.md at its root.");
            return SkillInstallVerificationResult.Invalid(candidatePath, request.ExpectedName, errors);
        }

        var content = await File.ReadAllTextAsync(skillFile, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        var skillName = string.IsNullOrWhiteSpace(request.ExpectedName)
            ? ExtractFrontmatterField(content, "name")
            : request.ExpectedName.Trim();

        if (string.IsNullOrWhiteSpace(skillName))
            errors.Add("Frontmatter must include a 'name' field, or --name must be provided.");
        else if (SkillFrontmatter.ValidateName(skillName) is { } nameError)
            errors.Add(nameError);

        if (!string.IsNullOrWhiteSpace(skillName)
            && SkillFrontmatter.ValidateContent(
                content,
                skillName,
                request.MaxSkillContentChars) is { } contentError)
        {
            errors.Add(contentError);
        }

        ValidateBundleFiles(candidatePath, errors);

        return errors.Count == 0
            ? SkillInstallVerificationResult.Valid(candidatePath, skillName!, skillFile)
            : SkillInstallVerificationResult.Invalid(candidatePath, skillName, errors);
    }

    /// <summary>
    /// Verifies and installs a candidate skill bundle into the workspace skill source root.
    /// </summary>
    public async Task<SkillInstallResult> InstallAsync(
        SkillInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var verification = await VerifyAsync(
            new SkillInstallVerifyRequest(
                request.CandidatePath,
                request.ExpectedName,
                request.MaxSkillContentChars),
            cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid || string.IsNullOrWhiteSpace(verification.SkillName))
        {
            return SkillInstallResult.Fail(verification.CandidatePath, verification.SkillName, verification.Errors);
        }

        var skillName = verification.SkillName;
        var existing = skillsLoader.ListSkills(filterUnavailable: false)
            .FirstOrDefault(skill => string.Equals(skill.Name, skillName, StringComparison.OrdinalIgnoreCase));
        if (existing != null && !request.Overwrite)
        {
            return SkillInstallResult.Fail(
                verification.CandidatePath,
                skillName,
                [$"A skill named '{skillName}' already exists at {existing.Path}. Use --overwrite to replace the workspace source."]);
        }

        var targetDir = skillsLoader.ResolveWorkspaceSkillDir(skillName);
        var targetIsBuiltIn = File.Exists(Path.Combine(targetDir, ".builtin"));
        if (targetIsBuiltIn)
        {
            return SkillInstallResult.Fail(
                verification.CandidatePath,
                skillName,
                [$"Skill '{skillName}' is a built-in skill and cannot be overwritten."]);
        }

        var skillsRoot = skillsLoader.WorkspaceSkillsPath;
        Directory.CreateDirectory(skillsRoot);
        var stagingDir = Path.Combine(skillsRoot, $".install-{skillName}-{Guid.NewGuid():N}");
        var backupDir = Path.Combine(skillsRoot, $".backup-{skillName}-{Guid.NewGuid():N}");
        var movedExisting = false;

        try
        {
            CopyValidatedBundle(verification.CandidatePath, stagingDir);

            var stagingSkillFile = Path.Combine(stagingDir, "SKILL.md");
            var fingerprint = SkillVariantStore.ComputeSourceFingerprint(stagingSkillFile);
            var marker = new SkillInstallMarker
            {
                Source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim(),
                InstalledAt = DateTimeOffset.UtcNow,
                SourceFingerprint = fingerprint
            };
            await File.WriteAllTextAsync(
                Path.Combine(stagingDir, InstallMarkerFileName),
                JsonSerializer.Serialize(marker, JsonOptions) + Environment.NewLine,
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(targetDir))
            {
                Directory.Move(targetDir, backupDir);
                movedExisting = true;
            }

            Directory.Move(stagingDir, targetDir);

            if (movedExisting && Directory.Exists(backupDir))
                Directory.Delete(backupDir, recursive: true);

            skillsLoader.RefreshDescriptors();
            return SkillInstallResult.Ok(
                verification.CandidatePath,
                skillName,
                targetDir,
                fingerprint,
                movedExisting);
        }
        catch
        {
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, recursive: true);
            if (movedExisting && Directory.Exists(backupDir) && !Directory.Exists(targetDir))
                Directory.Move(backupDir, targetDir);
            throw;
        }
        finally
        {
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, recursive: true);
        }
    }

    private static void ValidateBundleFiles(string candidatePath, List<string> errors)
    {
        var root = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToArray();

        if (files.Length > MaxFileCount)
            errors.Add($"Skill bundle has too many files ({files.Length:N0}; limit: {MaxFileCount:N0}).");

        long totalBytes = 0;
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                errors.Add($"File escapes candidate directory: {file}");
                continue;
            }

            var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
            {
                errors.Add($"Invalid skill file path: {relative}");
                continue;
            }

            if (!IsAllowedSkillPath(relative, parts))
                errors.Add($"Unsupported skill file path: {relative}");

            var length = new FileInfo(file).Length;
            if (length > SkillFrontmatter.DefaultMaxSupportingFileBytes && !string.Equals(relative, "SKILL.md", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Supporting file is too large: {relative} ({length:N0} bytes; limit: {SkillFrontmatter.DefaultMaxSupportingFileBytes:N0}).");
            totalBytes += length;
        }

        if (totalBytes > MaxTotalBytes)
            errors.Add($"Skill bundle is too large ({totalBytes:N0} bytes; limit: {MaxTotalBytes:N0}).");
    }

    private static bool IsAllowedSkillPath(string relative, string[] parts)
    {
        if (string.Equals(relative, "SKILL.md", StringComparison.Ordinal))
            return true;
        if (string.Equals(relative, InstallMarkerFileName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (parts[0].StartsWith(".", StringComparison.Ordinal))
            return false;
        if (parts[0].Equals("scripts", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("assets", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length >= 2;
        }

        return parts.Length == 2
               && parts[0].Equals("agents", StringComparison.OrdinalIgnoreCase)
               && parts[1].Equals("openai.yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyValidatedBundle(string sourceDir, string targetDir)
    {
        var root = Path.GetFullPath(sourceDir);
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (string.Equals(relative, InstallMarkerFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            var target = Path.GetFullPath(Path.Combine(targetDir, relative));
            var normalizedTargetDir = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!target.StartsWith(normalizedTargetDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"File escapes install directory: {relative}");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string? ExtractFrontmatterField(string content, string key)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
            return null;

        using var reader = new StringReader(content);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
            return null;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
                return null;

            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            var currentKey = line[..separator].Trim();
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                continue;

            return line[(separator + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }

    private sealed class SkillInstallMarker
    {
        public string? Source { get; init; }

        public DateTimeOffset InstalledAt { get; init; }

        public string SourceFingerprint { get; init; } = string.Empty;
    }
}

/// <summary>
/// Request for candidate skill verification.
/// </summary>
public sealed record SkillInstallVerifyRequest(
    string CandidatePath,
    string? ExpectedName = null,
    int MaxSkillContentChars = SkillFrontmatter.DefaultMaxSkillContentChars);

/// <summary>
/// Result of candidate skill verification.
/// </summary>
public sealed record SkillInstallVerificationResult(
    bool IsValid,
    string CandidatePath,
    string? SkillName,
    string? SkillFilePath,
    IReadOnlyList<string> Errors)
{
    public static SkillInstallVerificationResult Valid(string candidatePath, string skillName, string skillFilePath) =>
        new(true, candidatePath, skillName, skillFilePath, []);

    public static SkillInstallVerificationResult Invalid(string candidatePath, string? skillName, IReadOnlyList<string> errors) =>
        new(false, candidatePath, skillName, null, errors);
}

/// <summary>
/// Request for publishing a candidate skill into the workspace source root.
/// </summary>
public sealed record SkillInstallRequest(
    string CandidatePath,
    string? ExpectedName = null,
    bool Overwrite = false,
    string? Source = null,
    int MaxSkillContentChars = SkillFrontmatter.DefaultMaxSkillContentChars);

/// <summary>
/// Result of publishing a candidate skill.
/// </summary>
public sealed record SkillInstallResult(
    bool Success,
    string CandidatePath,
    string? SkillName,
    string? TargetDir,
    string? SourceFingerprint,
    bool Overwritten,
    IReadOnlyList<string> Errors)
{
    public static SkillInstallResult Ok(
        string candidatePath,
        string skillName,
        string targetDir,
        string sourceFingerprint,
        bool overwritten) =>
        new(true, candidatePath, skillName, targetDir, sourceFingerprint, overwritten, []);

    public static SkillInstallResult Fail(string candidatePath, string? skillName, IReadOnlyList<string> errors) =>
        new(false, candidatePath, skillName, null, null, false, errors);
}
