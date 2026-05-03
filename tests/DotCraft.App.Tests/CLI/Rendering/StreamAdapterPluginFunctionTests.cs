using System.Text.Json;
using DotCraft.CLI.Rendering;

namespace DotCraft.Tests.CLI.Rendering;

public sealed class StreamAdapterPluginFunctionTests
{
    [Fact]
    public async Task AdaptWireNotificationsAsync_MapsPluginFunctionCallLifecycleToToolEvents()
    {
        var events = new List<RenderEvent>();
        await foreach (var renderEvent in StreamAdapter.AdaptWireNotificationsAsync(ReadNotifications()))
        {
            events.Add(renderEvent);
        }

        Assert.Collection(
            events,
            started =>
            {
                Assert.Equal(RenderEventType.ToolCallStarted, started.Type);
                Assert.Equal("NodeReplJs", started.Title);
                Assert.Equal("plugin-call-1", started.CallId);
                Assert.Contains("\"code\"", started.AdditionalInfo);
            },
            completed =>
            {
                Assert.Equal(RenderEventType.ToolCallCompleted, completed.Type);
                Assert.Equal("NodeReplJs", completed.Title);
                Assert.Equal("plugin-call-1", completed.CallId);
                Assert.Equal($"2{Environment.NewLine}[image: image/png]", completed.AdditionalInfo);
            });
    }

    private static async IAsyncEnumerable<JsonDocument> ReadNotifications()
    {
        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/started",
              "params": {
                "item": {
                  "id": "plugin-1",
                  "type": "pluginFunctionCall",
                  "payload": {
                    "pluginId": "browser-use",
                    "namespace": "node_repl",
                    "functionName": "NodeReplJs",
                    "callId": "plugin-call-1",
                    "arguments": { "code": "1 + 1" }
                  }
                }
              }
            }
            """);

        await Task.Yield();

        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/completed",
              "params": {
                "item": {
                  "id": "plugin-1",
                  "type": "pluginFunctionCall",
                  "payload": {
                    "pluginId": "browser-use",
                    "namespace": "node_repl",
                    "functionName": "NodeReplJs",
                    "callId": "plugin-call-1",
                    "contentItems": [
                      { "type": "text", "text": "2" },
                      { "type": "image", "mediaType": "image/png", "dataBase64": "abc123" }
                    ],
                    "success": true
                  }
                }
              }
            }
            """);
    }
}
