namespace DotCraft.Sessions.Protocol;

/// <summary>
/// Generates stable, human-readable IDs for Session Protocol entities.
/// </summary>
public static class SessionIdGenerator
{
    private static readonly Random _random = new();
    private const string Chars = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Generates a new Thread ID.
    /// Format: thread_{yyyyMMdd}_{6-char-random}, e.g. "thread_20260315_a3f2k9".
    /// </summary>
    public static string NewThreadId()
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var random = GenerateRandom(6);
        return $"thread_{date}_{random}";
    }

    /// <summary>
    /// Generates a Turn ID for the given 1-based sequence number within a Thread.
    /// Format: turn_{3-digit-sequence}, e.g. "turn_001".
    /// </summary>
    public static string NewTurnId(int sequence) =>
        $"turn_{sequence:D3}";

    /// <summary>
    /// Generates an Item ID for the given 1-based sequence number within a Turn.
    /// Format: item_{3-digit-sequence}, e.g. "item_001".
    /// </summary>
    public static string NewItemId(int sequence) =>
        $"item_{sequence:D3}";

    private static string GenerateRandom(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Chars[_random.Next(Chars.Length)];
        return new string(chars);
    }
}
