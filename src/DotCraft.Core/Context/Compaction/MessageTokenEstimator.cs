using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Rough token estimator for <see cref="ChatMessage"/> sequences. Models
/// openclaude's <c>estimateMessageTokens</c> / <c>roughTokenCountEstimation</c>.
/// The estimate pads by 4/3 so callers can use it as a conservative upper bound.
/// </summary>
public static class MessageTokenEstimator
{
    /// <summary>
    /// Approximate tokens-per-character ratio for latin-plus-CJK text.
    /// Lifted from openclaude; the 4/3 pad applied in <see cref="Estimate"/>
    /// offsets the typical underestimate for tokenizer-heavy payloads.
    /// </summary>
    private const double CharsPerToken = 4.0;

    /// <summary>
    /// Fixed token cost for an image or document content part.
    /// </summary>
    private const int ImageTokenCost = 2000;

    /// <summary>
    /// Returns the estimated token count for a single content block.
    /// </summary>
    public static int EstimateContent(AIContent content)
    {
        return content switch
        {
            TextContent tc => RoughTokenCount(tc.Text),
            DataContent dc when IsImage(dc) => ImageTokenCost,
            UriContent uc when IsImage(uc) => ImageTokenCost,
            FunctionCallContent fc =>
                RoughTokenCount(fc.Name)
                + RoughTokenCount(SerializeArguments(fc.Arguments)),
            FunctionResultContent fr =>
                RoughTokenCount(SerializeResult(fr.Result)),
            _ => RoughTokenCount(content.ToString() ?? string.Empty),
        };
    }

    /// <summary>
    /// Estimates the token cost of a single <see cref="ChatMessage"/>.
    /// </summary>
    public static int EstimateMessage(ChatMessage message)
    {
        var total = 0;
        if (!string.IsNullOrEmpty(message.AuthorName))
            total += RoughTokenCount(message.AuthorName);

        foreach (var content in message.Contents)
            total += EstimateContent(content);

        return total;
    }

    /// <summary>
    /// Estimates the token cost of a message sequence with a 4/3 safety pad
    /// (matching openclaude's <c>estimateMessageTokens</c>).
    /// </summary>
    public static int Estimate(IReadOnlyList<ChatMessage> messages)
    {
        var total = 0L;
        foreach (var message in messages)
            total += EstimateMessage(message);

        return (int)Math.Min(int.MaxValue, Math.Ceiling(total * 4.0 / 3.0));
    }

    /// <summary>
    /// Unpadded character-based token estimate for a raw string. Exposed so
    /// the microcompact path can compare content deltas without the 4/3 pad.
    /// </summary>
    public static int RoughTokenCount(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / CharsPerToken);
    }

    private static bool IsImage(DataContent content)
    {
        var mediaType = content.MediaType;
        return !string.IsNullOrEmpty(mediaType) &&
               mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImage(UriContent content)
    {
        var mediaType = content.MediaType;
        return !string.IsNullOrEmpty(mediaType) &&
               mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static string SerializeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return string.Empty;

        try
        {
            return JsonSerializer.Serialize(arguments);
        }
        catch
        {
            // Fall back to a character-only estimate of the dict keys so an
            // unserializable payload still contributes to the total.
            var total = 0;
            foreach (var key in arguments.Keys)
                total += key.Length + 2;
            return new string('x', total);
        }
    }

    private static string SerializeResult(object? result)
    {
        if (result is null)
            return string.Empty;

        if (result is string s)
            return s;

        try
        {
            return JsonSerializer.Serialize(result);
        }
        catch
        {
            return result.ToString() ?? string.Empty;
        }
    }
}
