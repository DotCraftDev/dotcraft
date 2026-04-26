using DotCraft.Abstractions;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Provides Desktop-hosted Node REPL tools when the current thread is bound
/// to a browser-use capable AppServer client.
/// </summary>
public sealed class NodeReplToolProvider : IAgentToolProvider
{
    public int Priority => 120;

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var proxy = context.NodeReplProxy;
        if (proxy?.IsAvailable != true)
            yield break;

        var tools = new NodeReplTools(proxy);
        yield return AIFunctionFactory.Create(tools.NodeReplJs);
        yield return AIFunctionFactory.Create(tools.NodeReplReset);
    }

    private sealed class NodeReplTools(INodeReplProxy proxy)
    {
        /// <summary>
        /// Evaluate JavaScript in the Desktop persistent Node REPL for the current thread.
        /// The runtime supports top-level state, agent.browser, display(), and screenshot image output.
        /// </summary>
        public async Task<IReadOnlyList<AIContent>> NodeReplJs(
            string code,
            int? timeoutSeconds = null,
            CancellationToken ct = default)
        {
            var result = await proxy.EvaluateAsync(code, timeoutSeconds, ct);
            if (result == null)
                return [new TextContent("Node REPL browser runtime is not available for this thread.")];

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
                : "(Node REPL completed with no text output)"));

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
                    contents.Add(new TextContent("[Invalid Node REPL image payload]"));
                }
            }

            return contents;
        }

        /// <summary>
        /// Reset the Desktop Node REPL JavaScript context and close browser tabs for the current thread.
        /// </summary>
        public async Task<string> NodeReplReset(CancellationToken ct = default)
        {
            var ok = await proxy.ResetAsync(ct);
            return ok ? "Node REPL browser runtime reset." : "Node REPL browser runtime is not available for this thread.";
        }
    }
}
