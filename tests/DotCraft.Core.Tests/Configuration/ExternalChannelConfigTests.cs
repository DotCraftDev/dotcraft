using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Configuration;

namespace DotCraft.Tests.Configuration;

public class ExternalChannelConfigTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void ExternalChannelsSection_HasRootKey_ItemFields_AndExpectedTypes()
    {
        var schema = ConfigSchemaBuilder.BuildAll([typeof(ExternalChannelEntry)]);
        var section = Assert.Single(schema);

        Assert.Equal("ExternalChannels", section.RootKey);
        Assert.NotNull(section.ItemFields);
        Assert.NotEmpty(section.ItemFields);
        Assert.Empty(section.Fields);

        var byKey = section.ItemFields!.ToDictionary(f => f.Key, f => f);
        Assert.Equal("text", byKey["Name"].Type);
        Assert.Equal("bool", byKey["Enabled"].Type);
        Assert.Equal("select", byKey["Transport"].Type);
        Assert.Contains("subprocess", byKey["Transport"].Options!);
        Assert.Contains("websocket", byKey["Transport"].Options!);
        Assert.Equal("text", byKey["Command"].Type);
        Assert.Equal("stringList", byKey["Args"].Type);
        Assert.Equal("text", byKey["WorkingDirectory"].Type);
        Assert.Equal("keyValueMap", byKey["Env"].Type);
    }

    [Fact]
    public void AppConfig_Deserializes_ExternalChannels_ObjectMap_IntoStrongTypedList()
    {
        const string json = """
        {
          "ExternalChannels": {
            "telegram": {
              "enabled": true,
              "transport": "subprocess",
              "command": "python",
              "args": ["-m", "dotcraft_telegram"],
              "workingDirectory": "./adapters/telegram",
              "env": {
                "TELEGRAM_BOT_TOKEN": "secret"
              }
            },
            "weixin": {
              "enabled": true,
              "transport": "websocket"
            }
          }
        }
        """;

        var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);

        Assert.NotNull(config);
        Assert.Equal(2, config!.ExternalChannels.Count);

        var telegram = Assert.Single(config.ExternalChannels, c => c.Name == "telegram");
        Assert.True(telegram.Enabled);
        Assert.Equal(ExternalChannelTransport.Subprocess, telegram.Transport);
        Assert.Equal("python", telegram.Command);
        Assert.Equal(["-m", "dotcraft_telegram"], telegram.Args);
        Assert.Equal("./adapters/telegram", telegram.WorkingDirectory);
        Assert.Equal("secret", telegram.Env!["TELEGRAM_BOT_TOKEN"]);

        var weixin = Assert.Single(config.ExternalChannels, c => c.Name == "weixin");
        Assert.Equal(ExternalChannelTransport.Websocket, weixin.Transport);
    }

    [Fact]
    public void AppConfig_Serializes_ExternalChannels_AsObjectMap_KeyedByName()
    {
        var config = new AppConfig
        {
            ExternalChannels =
            [
                new ExternalChannelEntry
                {
                    Name = "telegram",
                    Enabled = true,
                    Transport = ExternalChannelTransport.Subprocess,
                    Command = "python",
                    Args = ["-m", "dotcraft_telegram"],
                    WorkingDirectory = "./adapters/telegram",
                    Env = new Dictionary<string, string> { ["TELEGRAM_BOT_TOKEN"] = "secret" }
                },
                new ExternalChannelEntry
                {
                    Name = "weixin",
                    Enabled = true,
                    Transport = ExternalChannelTransport.Websocket
                }
            ]
        };

        var node = JsonSerializer.SerializeToNode(config, SerializerOptions) as JsonObject;
        Assert.NotNull(node);

        var external = Assert.IsType<JsonObject>(node!["ExternalChannels"]);
        Assert.NotNull(external["telegram"]);
        Assert.NotNull(external["weixin"]);

        var telegram = Assert.IsType<JsonObject>(external["telegram"]);
        Assert.False(telegram.ContainsKey("Name"));
        Assert.Equal("python", telegram["Command"]?.GetValue<string>());

        var weixin = Assert.IsType<JsonObject>(external["weixin"]);
        Assert.False(weixin.ContainsKey("Name"));
        Assert.Equal("Websocket", weixin["Transport"]?.GetValue<string>());
    }
}
