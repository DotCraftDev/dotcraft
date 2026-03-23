using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Automations.Abstractions;
using DotCraft.Hosting;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotCraft.Automations.Local;

/// <summary>
/// Reads and writes local <c>task.md</c> files (YAML front matter + Markdown body).
/// </summary>
public sealed partial class LocalTaskFileStore(
    AutomationsConfig config,
    DotCraftPaths paths,
    ILogger<LocalTaskFileStore> logger)
{
    private static readonly Regex FrontMatterRegex = GetFrontMatterRegex();

    /// <summary>Resolved absolute path to the tasks root directory.</summary>
    public string TasksRoot { get; } = string.IsNullOrWhiteSpace(config.LocalTasksRoot)
        ? Path.Combine(paths.WorkspacePath, ".craft", "tasks")
        : Path.GetFullPath(config.LocalTasksRoot);

    /// <inheritdoc cref="LoadAllAsync"/>
    public Task<IReadOnlyList<LocalAutomationTask>> LoadAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(TasksRoot);
        var result = new List<LocalAutomationTask>();

        foreach (var dir in Directory.EnumerateDirectories(TasksRoot))
        {
            var taskFile = Path.Combine(dir, "task.md");
            if (!File.Exists(taskFile))
                continue;

            try
            {
                result.Add(LoadFromFile(dir));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load task from {Dir}", dir);
            }
        }

        return Task.FromResult<IReadOnlyList<LocalAutomationTask>>(result);
    }

    /// <inheritdoc cref="LoadAsync"/>
    public Task<LocalAutomationTask> LoadAsync(string taskDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(LoadFromFile(taskDirectory));
    }

    private LocalAutomationTask LoadFromFile(string taskDirectory)
    {
        var taskFile = Path.Combine(taskDirectory, "task.md");
        if (!File.Exists(taskFile))
            throw new FileNotFoundException($"task.md not found: {taskFile}");

        var content = File.ReadAllText(taskFile);
        return ParseTaskFile(Path.GetFullPath(taskDirectory), content);
    }

    internal LocalAutomationTask ParseTaskFile(string taskDirectory, string content)
    {
        var match = FrontMatterRegex.Match(content);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"task.md must start with YAML front matter (--- ... ---): {taskDirectory}");
        }

        var yamlText = match.Groups[1].Value;
        var body = match.Groups[2].Value.TrimEnd();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithTypeConverter(new DateTimeOffsetIsoYamlConverter())
            .Build();

        var fm = deserializer.Deserialize<TaskFileFrontMatter>(yamlText);
        var status = LocalTaskStatusMapping.FromYaml(fm.Status);

        return new LocalAutomationTask
        {
            TaskDirectory = taskDirectory,
            Id = fm.Id ?? Path.GetFileName(taskDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Title = fm.Title ?? fm.Id ?? "Untitled",
            Status = status,
            SourceName = "local",
            ThreadId = fm.ThreadId,
            Description = body,
            AgentSummary = fm.AgentSummary,
            CreatedAt = fm.CreatedAt,
            UpdatedAt = fm.UpdatedAt,
            ApprovalPolicy = fm.ApprovalPolicy
        };
    }

    /// <summary>Persists status, thread id, and agent summary to task.md.</summary>
    public Task SaveAsync(LocalAutomationTask task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var taskFile = task.TaskFilePath;
        Directory.CreateDirectory(task.TaskDirectory);

        string body;
        if (File.Exists(taskFile))
        {
            var content = File.ReadAllText(taskFile);
            var match = FrontMatterRegex.Match(content);
            body = match.Success ? match.Groups[2].Value.TrimEnd() : content.Trim();
        }
        else
        {
            body = task.Description ?? string.Empty;
        }

        task.UpdatedAt = DateTimeOffset.UtcNow;
        if (task.CreatedAt == null)
            task.CreatedAt = task.UpdatedAt;

        var fm = new TaskFileFrontMatter
        {
            Id = task.Id,
            Title = task.Title,
            Status = LocalTaskStatusMapping.ToYaml(task.Status),
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            ThreadId = task.ThreadId,
            AgentSummary = task.AgentSummary,
            ApprovalPolicy = task.ApprovalPolicy
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithTypeConverter(new DateTimeOffsetIsoYamlConverter())
            .Build();

        var yaml = serializer.Serialize(fm);
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.Append(yaml.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine(body);

        File.WriteAllText(taskFile, sb.ToString());
        return Task.CompletedTask;
    }

    /// <summary>Watches for new task directories and task.md files.</summary>
    public IDisposable WatchForNewTasks(Action<LocalAutomationTask> onNewTask)
    {
        Directory.CreateDirectory(TasksRoot);
        var watcher = new FileSystemWatcher(TasksRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
            Filter = "*.*",
            EnableRaisingEvents = true
        };

        void TryHandle(string fullPath)
        {
            try
            {
                if (!string.Equals(Path.GetFileName(fullPath), "task.md", StringComparison.OrdinalIgnoreCase))
                    return;

                var dir = Path.GetDirectoryName(fullPath);
                if (dir == null || !dir.StartsWith(TasksRoot, StringComparison.OrdinalIgnoreCase))
                    return;

                if (!File.Exists(fullPath))
                    return;

                var task = LoadFromFile(dir);
                onNewTask(task);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Watch callback skipped for {Path}", fullPath);
            }
        }

        watcher.Created += (_, e) => TryHandle(e.FullPath);
        watcher.Renamed += (_, e) => TryHandle(e.FullPath);

        return watcher;
    }

    private sealed class TaskFileFrontMatter
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? ThreadId { get; set; }
        public string? AgentSummary { get; set; }

        /// <summary><c>autoApprove</c> or <c>default</c>.</summary>
        public string? ApprovalPolicy { get; set; }
    }

    [GeneratedRegex(@"^---\s*\r?\n(.*?)---\s*\r?\n(.*)$", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex GetFrontMatterRegex();
}
