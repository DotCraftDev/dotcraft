using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Plugins;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Provides Desktop-hosted Node REPL runtime tools when the current thread is bound
/// to a browser-use capable AppServer client.
/// </summary>
public sealed class NodeReplPluginFunctionProvider : IAgentToolProvider
{
    public const string PluginId = PluginIds.BrowserUse;

    public int Priority => 120;

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        if (!IsBrowserUsePluginEnabledAndInstalled(context))
            yield break;

        var proxy = context.NodeReplProxy;
        if (proxy?.IsAvailable != true)
            yield break;

        yield return new PluginFunctionRuntimeFunction(
            new PluginFunctionRegistration(
                CreateDescriptor(),
                new NodeReplJsInvoker(proxy)));

    }

    private static bool IsBrowserUsePluginEnabledAndInstalled(ToolProviderContext context) =>
        context.Config.Plugins.IsPluginEnabled(PluginId, defaultEnabled: true)
        && PluginRuntimeConfigurator.IsPluginInstalledAndEnabled(
            context.Config,
            context.WorkspacePath,
            context.BotPath,
            PluginId);

    private static PluginFunctionDescriptor CreateDescriptor() =>
        new()
        {
            PluginId = PluginId,
            Namespace = "node_repl",
            Name = "NodeReplJs",
            Description = "Evaluate JavaScript in the Desktop persistent Node REPL for the current thread. The runtime supports top-level state, agent.browser, display(), and screenshot image output.",
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["code"] = new JsonObject { ["type"] = "string" },
                    ["timeoutSeconds"] = new JsonObject { ["type"] = "integer" }
                },
                ["required"] = new JsonArray("code")
            }
        };

    private sealed class NodeReplJsInvoker(INodeReplProxy proxy) : IPluginFunctionInvoker
    {
        public async ValueTask<PluginFunctionInvocationResult> InvokeAsync(
            PluginFunctionInvocationContext context,
            CancellationToken cancellationToken)
        {
            var code = context.Arguments["code"]?.GetValue<string>();
            int? timeoutSeconds = null;
            if (context.Arguments.TryGetPropertyValue("timeoutSeconds", out var timeoutNode)
                && timeoutNode?.GetValueKind() == JsonValueKind.Number)
            {
                timeoutSeconds = timeoutNode.GetValue<int>();
            }

            var result = await proxy.EvaluateAsync(code ?? string.Empty, timeoutSeconds, cancellationToken);
            if (result == null)
            {
                return new PluginFunctionInvocationResult
                {
                    Success = false,
                    ErrorCode = "NodeReplUnavailable",
                    ErrorMessage = "Node REPL browser runtime is not available for this thread.",
                    ContentItems =
                    [
                        new PluginFunctionContentItem
                        {
                            Type = "text",
                            Text = "Node REPL browser runtime is not available for this thread."
                        }
                    ]
                };
            }

            var contentItems = new List<PluginFunctionContentItem>();
            var textParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Text))
                textParts.Add(result.Text);
            if (!string.IsNullOrWhiteSpace(result.ResultText))
                textParts.Add(result.ResultText);
            if (result.Logs.Count > 0)
                textParts.Add(string.Join("\n", result.Logs));
            if (!string.IsNullOrWhiteSpace(result.Error))
                textParts.Add("Error: " + result.Error);

            contentItems.Add(new PluginFunctionContentItem
            {
                Type = "text",
                Text = textParts.Count > 0
                    ? string.Join("\n", textParts)
                    : "(Node REPL completed with no text output)"
            });

            foreach (var image in result.Images)
            {
                if (string.IsNullOrWhiteSpace(image.DataBase64))
                    continue;

                contentItems.Add(new PluginFunctionContentItem
                {
                    Type = "image",
                    DataBase64 = image.DataBase64,
                    MediaType = image.MediaType
                });
            }

            return new PluginFunctionInvocationResult
            {
                Success = string.IsNullOrWhiteSpace(result.Error),
                ErrorCode = string.IsNullOrWhiteSpace(result.Error) ? null : "NodeReplError",
                ErrorMessage = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error,
                ContentItems = contentItems
            };
        }
    }

}
