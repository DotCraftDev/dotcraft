using DotCraft.Abstractions;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Provides Desktop-hosted browser automation tools when the current thread is bound
/// to a browser-use capable AppServer client.
/// </summary>
public sealed class BrowserUseToolProvider : IAgentToolProvider
{
    public int Priority => 120;

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var proxy = context.BrowserUseProxy;
        if (proxy?.IsAvailable != true)
            yield break;

        var tools = new BrowserUseTools(proxy);
        yield return AIFunctionFactory.Create(tools.BrowserJs);
        yield return AIFunctionFactory.Create(tools.BrowserJsReset);
    }

    private sealed class BrowserUseTools(IBrowserUseProxy proxy)
    {
        /// <summary>
        /// Evaluate JavaScript in the Desktop browser-use runtime for the current thread.
        /// The runtime exposes agent.browser and display(), and can return text plus screenshots.
        /// </summary>
        public async Task<IReadOnlyList<AIContent>> BrowserJs(
            string code,
            int? timeoutSeconds = null,
            CancellationToken ct = default)
        {
            var result = await proxy.EvaluateAsync(code, timeoutSeconds, ct);
            if (result == null)
                return [new TextContent("Browser-use is not available for this thread.")];

            var contents = new List<AIContent>();
            var textParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Text))
                textParts.Add(result.Text);
            if (!string.IsNullOrWhiteSpace(result.ResultText))
                textParts.Add(result.ResultText);
            if (result.Logs.Count > 0)
                textParts.Add(string.Join("\n", result.Logs));
            if (!string.IsNullOrWhiteSpace(result.Error))
                textParts.Add("Error: " + result.Error);

            contents.Add(new TextContent(textParts.Count > 0
                ? string.Join("\n", textParts)
                : "(browser-use completed with no text output)"));

            foreach (var image in result.Images)
            {
                if (string.IsNullOrWhiteSpace(image.DataBase64))
                    continue;
                try
                {
                    contents.Add(new DataContent(Convert.FromBase64String(image.DataBase64), image.MediaType));
                }
                catch (FormatException)
                {
                    contents.Add(new TextContent("[Invalid browser-use image payload]"));
                }
            }

            return contents;
        }

        /// <summary>
        /// Reset the Desktop browser-use JavaScript context and close browser tabs for the current thread.
        /// </summary>
        public async Task<string> BrowserJsReset(CancellationToken ct = default)
        {
            var ok = await proxy.ResetAsync(ct);
            return ok ? "Browser-use runtime reset." : "Browser-use is not available for this thread.";
        }
    }
}
