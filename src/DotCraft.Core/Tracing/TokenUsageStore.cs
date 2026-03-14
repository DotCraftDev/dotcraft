using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotCraft.Tracing;

public sealed class TokenUsageRecord
{
    /// <summary>
    /// The channel that produced this record, e.g. "qq", "wecom", "api", "cli".
    /// </summary>
    public string Channel { get; init; } = string.Empty;

    public string UserId { get; init; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Non-null when the message originated from a group context.
    /// </summary>
    public long? GroupId { get; init; }

    public string? GroupName { get; set; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class UserTokenUsage
{
    public string UserId { get; init; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    private long _totalInputTokens;
    private long _totalOutputTokens;
    private int _requestCount;

    public long TotalInputTokens => Interlocked.Read(ref _totalInputTokens);

    public long TotalOutputTokens => Interlocked.Read(ref _totalOutputTokens);

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public int RequestCount => Interlocked.CompareExchange(ref _requestCount, 0, 0);

    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.UtcNow;

    public void Add(long inputTokens, long outputTokens)
    {
        Interlocked.Add(ref _totalInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
        Interlocked.Increment(ref _requestCount);
        LastActiveAt = DateTimeOffset.UtcNow;
    }

    public void Load(long inputTokens, long outputTokens, int requestCount, DateTimeOffset lastActiveAt)
    {
        Interlocked.Add(ref _totalInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
        Interlocked.Add(ref _requestCount, requestCount);
        if (lastActiveAt > LastActiveAt)
            LastActiveAt = lastActiveAt;
    }
}

public sealed class GroupTokenUsage
{
    public long GroupId { get; init; }

    public string GroupName { get; set; } = string.Empty;

    public ConcurrentDictionary<string, UserTokenUsage> Users { get; } = new();

    public long TotalInputTokens => Users.Values.Sum(u => u.TotalInputTokens);

    public long TotalOutputTokens => Users.Values.Sum(u => u.TotalOutputTokens);

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public int TotalRequestCount => Users.Values.Sum(u => u.RequestCount);

    public DateTimeOffset LastActiveAt => Users.Values.Count > 0
        ? Users.Values.Max(u => u.LastActiveAt)
        : DateTimeOffset.MinValue;
}

/// <summary>
/// Aggregates token usage for a single channel (e.g. "qq", "wecom", "api").
/// Direct/private messages are stored in <see cref="Users"/>;
/// group messages are stored in <see cref="Groups"/>.
/// </summary>
public sealed class ChannelTokenUsage
{
    public string Channel { get; init; } = string.Empty;

    public ConcurrentDictionary<string, UserTokenUsage> Users { get; } = new();

    public ConcurrentDictionary<long, GroupTokenUsage> Groups { get; } = new();
}

public sealed class ChannelSummary
{
    public string Channel { get; init; } = string.Empty;

    public long TotalInputTokens { get; init; }

    public long TotalOutputTokens { get; init; }

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public int TotalRequests { get; init; }

    public int UserCount { get; init; }

    public int GroupCount { get; init; }
}

public sealed class TokenUsageStore(string? storagePath = null)
{
    private readonly ConcurrentDictionary<string, ChannelTokenUsage> _channels = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Record(TokenUsageRecord record)
    {
        if (string.IsNullOrEmpty(record.Channel))
            return;

        var channel = _channels.GetOrAdd(record.Channel, ch => new ChannelTokenUsage { Channel = ch });

        if (record.GroupId.HasValue)
        {
            var group = channel.Groups.GetOrAdd(record.GroupId.Value, id => new GroupTokenUsage { GroupId = id });
            if (!string.IsNullOrEmpty(record.GroupName))
                group.GroupName = record.GroupName;

            var groupUser = group.Users.GetOrAdd(record.UserId, _ => new UserTokenUsage { UserId = record.UserId });
            groupUser.DisplayName = record.DisplayName;
            groupUser.Add(record.InputTokens, record.OutputTokens);
        }
        else
        {
            var user = channel.Users.GetOrAdd(record.UserId, _ => new UserTokenUsage { UserId = record.UserId });
            user.DisplayName = record.DisplayName;
            user.Add(record.InputTokens, record.OutputTokens);
        }

        if (storagePath != null)
            PersistRecord(record);
    }

    public IReadOnlyList<ChannelTokenUsage> GetChannels()
    {
        return _channels.Values
            .OrderBy(c => c.Channel)
            .ToList();
    }

    public IReadOnlyList<UserTokenUsage> GetUsers(string channel)
    {
        if (!_channels.TryGetValue(channel, out var ch))
            return [];
        return ch.Users.Values
            .OrderByDescending(u => u.TotalTokens)
            .ToList();
    }

    public IReadOnlyList<GroupTokenUsage> GetGroups(string channel)
    {
        if (!_channels.TryGetValue(channel, out var ch))
            return [];
        return ch.Groups.Values
            .OrderByDescending(g => g.TotalTokens)
            .ToList();
    }

    public IReadOnlyList<ChannelSummary> GetSummary()
    {
        return _channels.Values
            .OrderBy(c => c.Channel)
            .Select(c =>
            {
                long input = 0, output = 0;
                int requests = 0;

                foreach (var u in c.Users.Values)
                {
                    input += u.TotalInputTokens;
                    output += u.TotalOutputTokens;
                    requests += u.RequestCount;
                }

                foreach (var g in c.Groups.Values)
                {
                    input += g.TotalInputTokens;
                    output += g.TotalOutputTokens;
                    requests += g.TotalRequestCount;
                }

                return new ChannelSummary
                {
                    Channel = c.Channel,
                    TotalInputTokens = input,
                    TotalOutputTokens = output,
                    TotalRequests = requests,
                    UserCount = c.Users.Count,
                    GroupCount = c.Groups.Count
                };
            })
            .ToList();
    }

    public void LoadFromDisk()
    {
        if (storagePath == null)
            return;

        var filePath = Path.Combine(storagePath, "token_usage.jsonl");
        if (!File.Exists(filePath))
            return;

        try
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var record = JsonSerializer.Deserialize<TokenUsageRecord>(line, JsonOptions);
                    if (record == null || string.IsNullOrEmpty(record.Channel))
                        continue;

                    var channel = _channels.GetOrAdd(record.Channel, ch => new ChannelTokenUsage { Channel = ch });

                    if (record.GroupId.HasValue)
                    {
                        var group = channel.Groups.GetOrAdd(record.GroupId.Value, id => new GroupTokenUsage { GroupId = id });
                        if (!string.IsNullOrEmpty(record.GroupName))
                            group.GroupName = record.GroupName;

                        var groupUser = group.Users.GetOrAdd(record.UserId, _ => new UserTokenUsage { UserId = record.UserId });
                        groupUser.DisplayName = record.DisplayName;
                        groupUser.Load(record.InputTokens, record.OutputTokens, 1, record.Timestamp);
                    }
                    else
                    {
                        var user = channel.Users.GetOrAdd(record.UserId, _ => new UserTokenUsage { UserId = record.UserId });
                        user.DisplayName = record.DisplayName;
                        user.Load(record.InputTokens, record.OutputTokens, 1, record.Timestamp);
                    }
                }
                catch
                {
                    // Skip corrupted lines
                }
            }
        }
        catch
        {
            // Skip corrupted file
        }
    }

    private void PersistRecord(TokenUsageRecord record)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(storagePath!);
                var filePath = Path.Combine(storagePath!, "token_usage.jsonl");
                var json = JsonSerializer.Serialize(record, JsonOptions);
                lock (_fileLock)
                {
                    File.AppendAllText(filePath, json + "\n");
                }
            }
            catch
            {
                // Silently ignore persistence errors
            }
        });
    }

    private readonly object _fileLock = new();
}
