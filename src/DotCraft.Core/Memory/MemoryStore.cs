using System.Text;
using System.Collections.Concurrent;

namespace DotCraft.Memory;

/// <summary>
/// Dual-layer memory: MEMORY.md (structured long-term facts, always in context) +
/// HISTORY.md (append-only grep-searchable event log, not in context).
/// </summary>
public sealed class MemoryStore
{
    private static readonly ConcurrentDictionary<string, object> StoreLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _memoryDir;
    
    private readonly string _longTermFile;

    private readonly string _historyFile;

    private readonly object _syncRoot;

    public MemoryStore(string workspaceRoot)
    {
        _memoryDir = Path.Combine(workspaceRoot, "memory");
        Directory.CreateDirectory(_memoryDir);
        _longTermFile = Path.Combine(_memoryDir, "MEMORY.md");
        _historyFile = Path.Combine(_memoryDir, "HISTORY.md");
        _syncRoot = StoreLocks.GetOrAdd(Path.GetFullPath(_memoryDir), static _ => new object());
    }

    /// <summary>
    /// Gets the path to the MEMORY.md file.
    /// </summary>
    public string LongTermFilePath => _longTermFile;

    /// <summary>
    /// Gets the path to the HISTORY.md file.
    /// </summary>
    public string HistoryFilePath => _historyFile;

    /// <summary>
    /// Read long-term memory (MEMORY.md).
    /// </summary>
    public string ReadLongTerm()
    {
        lock (_syncRoot)
        {
            return File.Exists(_longTermFile) ? File.ReadAllText(_longTermFile, Encoding.UTF8) : string.Empty;
        }
    }

    /// <summary>
    /// Write to long-term memory (MEMORY.md).
    /// </summary>
    public bool WriteLongTerm(string content)
    {
        lock (_syncRoot)
        {
            WriteLongTermAtomic(content);
            return true;
        }
    }

    /// <summary>
    /// Append a timestamped entry to HISTORY.md (grep-searchable event log).
    /// Each entry is a paragraph followed by a blank line.
    /// </summary>
    public bool AppendHistory(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return false;

        lock (_syncRoot)
        {
            AppendHistoryCore(entry);
            return true;
        }
    }

    /// <summary>
    /// Saves a consolidation result under the memory-store lock.
    /// </summary>
    public MemoryStoreConsolidationWriteResult SaveConsolidation(string? historyEntry, string? memoryUpdate)
    {
        lock (_syncRoot)
        {
            var historyWritten = false;
            var memoryWritten = false;

            if (!string.IsNullOrWhiteSpace(historyEntry))
            {
                AppendHistoryCore(historyEntry);
                historyWritten = true;
            }

            if (!string.IsNullOrWhiteSpace(memoryUpdate))
            {
                var current = File.Exists(_longTermFile)
                    ? File.ReadAllText(_longTermFile, Encoding.UTF8)
                    : string.Empty;
                if (!string.Equals(memoryUpdate, current, StringComparison.Ordinal))
                {
                    WriteLongTermAtomic(memoryUpdate);
                    memoryWritten = true;
                }
            }

            return new MemoryStoreConsolidationWriteResult(memoryWritten, historyWritten);
        }
    }

    /// <summary>
    /// Read the full HISTORY.md content (used during consolidation).
    /// </summary>
    public string ReadHistory()
    {
        lock (_syncRoot)
        {
            return File.Exists(_historyFile) ? File.ReadAllText(_historyFile, Encoding.UTF8) : string.Empty;
        }
    }

    /// <summary>
    /// Get combined memory context for agent (long-term memory only; HISTORY.md is searched on demand via grep).
    /// </summary>
    public string GetMemoryContext()
    {
        var longTerm = ReadLongTerm();
        return !string.IsNullOrWhiteSpace(longTerm) ? "## Long-term Memory\n" + longTerm : string.Empty;
    }

    private void AppendHistoryCore(string entry)
    {
        using var writer = new StreamWriter(_historyFile, append: true, Encoding.UTF8);
        writer.Write(entry.TrimEnd());
        writer.Write("\n\n");
    }

    private void WriteLongTermAtomic(string content)
    {
        Directory.CreateDirectory(_memoryDir);
        var tempFile = Path.Combine(_memoryDir, $".MEMORY.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, content, Encoding.UTF8);
        try
        {
            if (File.Exists(_longTermFile))
            {
                File.Replace(tempFile, _longTermFile, null);
            }
            else
            {
                File.Move(tempFile, _longTermFile);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}

/// <summary>
/// Describes which memory files changed during a consolidation write.
/// </summary>
public readonly record struct MemoryStoreConsolidationWriteResult(bool MemoryWritten, bool HistoryWritten)
{
    /// <summary>
    /// True when either MEMORY.md or HISTORY.md was changed.
    /// </summary>
    public bool AnyWritten => MemoryWritten || HistoryWritten;
}
