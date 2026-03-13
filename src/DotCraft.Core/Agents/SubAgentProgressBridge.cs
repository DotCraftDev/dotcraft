using System.Collections.Concurrent;

namespace DotCraft.Agents;

/// <summary>
/// Thread-safe static registry that relays real-time SubAgent progress
/// (current tool, accumulated tokens) from SubAgent execution threads
/// to the REPL Live Table renderer which polls every ~80ms.
/// </summary>
public static class SubAgentProgressBridge
{
    private static readonly ConcurrentDictionary<string, ProgressEntry> Entries = new();

    public sealed class ProgressEntry
    {
        public volatile string? CurrentTool;
        public volatile bool IsCompleted;
        private long _inputTokens;
        private long _outputTokens;

        public long InputTokens => Interlocked.Read(ref _inputTokens);
        public long OutputTokens => Interlocked.Read(ref _outputTokens);

        public void AddTokens(long input, long output)
        {
            Interlocked.Add(ref _inputTokens, input);
            Interlocked.Add(ref _outputTokens, output);
        }
    }

    public static ProgressEntry GetOrCreate(string key)
        => Entries.GetOrAdd(key, _ => new ProgressEntry());

    public static ProgressEntry? TryGet(string key)
        => Entries.GetValueOrDefault(key);

    public static void Remove(string key)
        => Entries.TryRemove(key, out _);
}
