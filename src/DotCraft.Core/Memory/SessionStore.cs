using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Memory;

/// <summary>
/// Manages conversation sessions using Microsoft.Agents.AI AgentSession.
/// Stores sessions as JSON files in the threads directory.
/// </summary>
public sealed class SessionStore
{
    private readonly string _sessionsDir;
    
    private readonly bool _compactSessions;

    private static readonly Random Random = new();
    
    private const string Chars = "abcdefghijklmnopqrstuvwxyz0123456789";

    public SessionStore(string workspaceRoot, bool compactSessions = true)
    {
        _sessionsDir = Path.Combine(workspaceRoot, "sessions");
        _compactSessions = compactSessions;
        Directory.CreateDirectory(_sessionsDir);
    }

    /// <summary>
    /// Generate a new short session ID.
    /// </summary>
    public static string GenerateSessionId()
    {
        var id = new char[6];
        for (int i = 0; i < id.Length; i++)
        {
            id[i] = Chars[Random.Next(Chars.Length)];
        }
        return new string(id);
    }

    /// <summary>
    /// Load an existing session or create a new one.
    /// </summary>
    public async Task<AgentSession> LoadOrCreateAsync(AIAgent agent, string sessionId, CancellationToken cancellationToken = default)
    {
        var path = GetSessionPath(sessionId);
        if (File.Exists(path))
        {
            await using var stream = File.OpenRead(path);
            var element = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonSerializerOptions.Web, cancellationToken: cancellationToken);
            return await agent.DeserializeSessionAsync(element, cancellationToken: cancellationToken);
        }

        return await agent.CreateSessionAsync(cancellationToken);
    }

    // Tool result content is truncated to this length to reduce storage while preserving structure.
    private const int ToolResultMaxChars = 500;

    /// <summary>
    /// Save a session to disk, compacting tool result content to reduce storage.
    /// </summary>
    public async Task SaveAsync(AIAgent agent, AgentSession session, string sessionId, CancellationToken cancellationToken = default)
    {
        var path = GetSessionPath(sessionId);
        if (_compactSessions)
            CompactSession(session);
        var serialized = await agent.SerializeSessionAsync(session, JsonSerializerOptions.Web, cancellationToken);
        var json = serialized.GetRawText();
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    /// <summary>
    /// Truncates tool result content in the session's chat history to reduce storage.
    /// Tool calls on assistant messages are preserved so the model retains context
    /// about what actions were taken. Only the (potentially large) result payloads
    /// are truncated, keeping the conversation structure valid for LLM replay.
    /// </summary>
    private static void CompactSession(AgentSession session)
    {
        var chatHistory = session.GetService<ChatHistoryProvider>();
        if (chatHistory is not InMemoryChatHistoryProvider memoryProvider)
            return;

        foreach (var msg in memoryProvider)
        {
            if (msg.Role != ChatRole.Tool)
                continue;

            foreach (var content in msg.Contents)
            {
                if (content is TextContent textContent && textContent.Text?.Length > ToolResultMaxChars)
                    textContent.Text = textContent.Text[..ToolResultMaxChars] + "\n... (truncated)";
            }
        }
    }

    /// <summary>
    /// Delete a session.
    /// </summary>
    public bool Delete(string sessionId)
    {
        var path = GetSessionPath(sessionId);
        var metadataPath = GetMetadataPath(sessionId);
        
        var deleted = false;
        if (File.Exists(path))
        {
            File.Delete(path);
            deleted = true;
        }
        
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }

        return deleted;
    }

    /// <summary>
    /// List all sessions.
    /// </summary>
    public List<SessionInfo> ListSessions()
    {
        var sessions = new List<SessionInfo>();

        if (!Directory.Exists(_sessionsDir))
            return sessions;

        foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
        {
            try
            {
                var sessionId = Path.GetFileNameWithoutExtension(file);
                
                // Check file update time
                var fileInfo = new FileInfo(file);

                sessions.Add(new SessionInfo
                {
                    Key = sessionId,
                    CreatedAt = fileInfo.CreationTimeUtc.ToString("O"),
                    UpdatedAt = fileInfo.LastWriteTimeUtc.ToString("O"),
                    FirstUserMessage = ExtractFirstUserMessage(file)
                });
            }
            catch
            {
                // Skip invalid files
            }
        }

        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    private string GetSessionPath(string sessionId)
    {
        var safe = string.Concat(sessionId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_sessionsDir, $"{safe}.json");
    }

    private string GetMetadataPath(string sessionId)
    {
        var safe = string.Concat(sessionId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_sessionsDir, $"{safe}.meta.json");
    }

    /// <summary>
    /// Session information for listing.
    /// </summary>
    public sealed class SessionInfo
    {
        public string Key { get; set; } = string.Empty;
        
        public string CreatedAt { get; set; } = string.Empty;
        
        public string UpdatedAt { get; set; } = string.Empty;

        public string? FirstUserMessage { get; set; }
    }

    /// <summary>
    /// Reads the session JSON file and extracts the text of the first user-role message.
    /// Returns null if the file cannot be parsed or contains no user messages.
    /// </summary>
    private static string? ExtractFirstUserMessage(string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            return FindFirstUserMessage(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindFirstUserMessage(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            // Check if this object looks like a messages array container
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var result = prop.Name.Equals("messages", StringComparison.OrdinalIgnoreCase)
                        ? TryExtractFromMessagesArray(prop.Value)
                        : FindFirstUserMessage(prop.Value);

                    if (result != null)
                        return result;
                }
                else if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var result = FindFirstUserMessage(prop.Value);
                    if (result != null)
                        return result;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var result = FindFirstUserMessage(item);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private static string? TryExtractFromMessagesArray(JsonElement array)
    {
        foreach (var msg in array.EnumerateArray())
        {
            if (msg.ValueKind != JsonValueKind.Object)
                continue;

            if (!msg.TryGetProperty("role", out var roleProp))
                continue;

            if (!roleProp.GetString()?.Equals("user", StringComparison.OrdinalIgnoreCase) ?? true)
                continue;

            // Try "text" shorthand property first
            if (msg.TryGetProperty("text", out var textProp))
            {
                var text = textProp.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return StripRuntimeContextFromText(text);
            }

            // Fall back to contents array
            if (msg.TryGetProperty("contents", out var contentsProp) && contentsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var content in contentsProp.EnumerateArray())
                {
                    if (content.ValueKind != JsonValueKind.Object)
                        continue;

                    if (content.TryGetProperty("text", out var ct))
                    {
                        var text = ct.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return StripRuntimeContextFromText(text);
                    }
                }
            }
        }

        return null;
    }

    private static string StripRuntimeContextFromText(string text)
    {
        var idx = text.IndexOf("\n\n[Runtime Context]", StringComparison.Ordinal);
        return idx >= 0 ? text[..idx] : text;
    }
}
