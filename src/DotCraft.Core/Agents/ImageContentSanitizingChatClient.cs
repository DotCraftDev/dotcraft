using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Replaces non-text content (images, binary data) in tool-result messages with
/// text descriptions before forwarding to the LLM API.
/// Many OpenAI-compatible endpoints reject non-text content in tool-role messages (HTTP 400).
/// </summary>
public sealed class ImageContentSanitizingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(SanitizeMessages(chatMessages), options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(SanitizeMessages(chatMessages), options, cancellationToken);
    }

    private static List<ChatMessage> SanitizeMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();

        foreach (var msg in messages)
        {
            bool needsSanitization = false;
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent frc && HasNonTextContent(frc.Result))
                {
                    needsSanitization = true;
                    break;
                }
            }

            if (!needsSanitization)
            {
                result.Add(msg);
                continue;
            }

            var newContents = new List<AIContent>(msg.Contents.Count);
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent frc && HasNonTextContent(frc.Result))
                    newContents.Add(new FunctionResultContent(frc.CallId, DescribeResult(frc.Result)));
                else
                    newContents.Add(content);
            }

            result.Add(new ChatMessage(msg.Role, newContents));
        }

        return result;
    }

    private static bool HasNonTextContent(object? result)
    {
        if (result is IEnumerable<AIContent> items)
        {
            foreach (var item in items)
            {
                if (item is not TextContent)
                    return true;
            }
        }

        return false;
    }

    public static string DescribeResult(object? result)
    {
        if (result is not IEnumerable<AIContent> items)
            return result?.ToString() ?? "(no output)";

        var parts = new List<string>();
        foreach (var item in items)
        {
            switch (item)
            {
                case TextContent tc:
                    if (!string.IsNullOrEmpty(tc.Text))
                        parts.Add(tc.Text);
                    break;
                case DataContent dc:
                {
                    var mediaType = dc.MediaType;
                    var size = dc.Data.Length;
                    parts.Add(mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        ? $"[Image ({mediaType}), {size:N0} bytes]"
                        : $"[Binary data ({mediaType}), {size:N0} bytes]");
                    break;
                }
                default:
                    var text = item.ToString();
                    if (!string.IsNullOrEmpty(text))
                        parts.Add(text);
                    break;
            }
        }

        return parts.Count > 0 ? string.Join("\n", parts) : "(no output)";
    }
}
