using System.Text;

namespace DotCraft.Memory;

/// <summary>
/// Dual-layer memory: MEMORY.md (structured long-term facts, always in context) +
/// HISTORY.md (append-only grep-searchable event log, not in context).
/// </summary>
public sealed class MemoryStore
{
    private readonly string _memoryDir;
    
    private readonly string _longTermFile;

    private readonly string _historyFile;

    public MemoryStore(string workspaceRoot)
    {
        _memoryDir = Path.Combine(workspaceRoot, "memory");
        Directory.CreateDirectory(_memoryDir);
        _longTermFile = Path.Combine(_memoryDir, "MEMORY.md");
        _historyFile = Path.Combine(_memoryDir, "HISTORY.md");
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
        return File.Exists(_longTermFile) ? File.ReadAllText(_longTermFile, Encoding.UTF8) : string.Empty;
    }

    /// <summary>
    /// Write to long-term memory (MEMORY.md).
    /// </summary>
    public void WriteLongTerm(string content)
    {
        File.WriteAllText(_longTermFile, content, Encoding.UTF8);
    }

    /// <summary>
    /// Append a timestamped entry to HISTORY.md (grep-searchable event log).
    /// Each entry is a paragraph followed by a blank line.
    /// </summary>
    public void AppendHistory(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return;

        using var writer = new StreamWriter(_historyFile, append: true, Encoding.UTF8);
        writer.Write(entry.TrimEnd());
        writer.Write("\n\n");
    }

    /// <summary>
    /// Read the full HISTORY.md content (used during consolidation).
    /// </summary>
    public string ReadHistory()
    {
        return File.Exists(_historyFile) ? File.ReadAllText(_historyFile, Encoding.UTF8) : string.Empty;
    }

    /// <summary>
    /// Get combined memory context for agent (long-term memory only; HISTORY.md is searched on demand via grep).
    /// </summary>
    public string GetMemoryContext()
    {
        var longTerm = ReadLongTerm();
        return !string.IsNullOrWhiteSpace(longTerm) ? "## Long-term Memory\n" + longTerm : string.Empty;
    }
}
