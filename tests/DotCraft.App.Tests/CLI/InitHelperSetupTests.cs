using System.Text.Json.Nodes;
using DotCraft.CLI;
using DotCraft.Localization;

namespace DotCraft.Tests.CLI;

public sealed class InitHelperSetupTests : IDisposable
{
    private readonly string tempRoot;

    public InitHelperSetupTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-setup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    [Fact]
    public void RunSetup_SaveToUserConfig_WritesGlobalConfigAndKeepsWorkspaceConfigEmpty()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            Language = Language.English,
            ApiKey = "sk-global",
            EndPoint = "https://example.com/v1",
            Model = "gpt-4o-mini",
            Profile = WorkspaceBootstrapProfile.Developer,
            SaveToUserConfig = true
        }, globalConfigPath);

        Assert.Equal(0, result);
        Assert.True(File.Exists(globalConfigPath));

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        Assert.Equal("English", globalNode["Language"]?.GetValue<string>());
        Assert.Equal("sk-global", globalNode["ApiKey"]?.GetValue<string>());
        Assert.Equal("https://example.com/v1", globalNode["EndPoint"]?.GetValue<string>());
        Assert.Equal("gpt-4o-mini", globalNode["Model"]?.GetValue<string>());

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("Language", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("ApiKey", workspaceNode.Select(p => p.Key));
        Assert.Contains("Developer Assistant", File.ReadAllText(Path.Combine(craftPath, "AGENTS.md")));
    }

    [Fact]
    public void RunSetup_WorkspaceOnly_WritesWorkspaceConfigAndDoesNotCreateGlobalConfig()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            Language = Language.Chinese,
            ApiKey = "sk-local",
            EndPoint = "https://local.example/v1",
            Model = "deepseek-chat",
            Profile = WorkspaceBootstrapProfile.PersonalAssistant,
            SaveToUserConfig = false
        }, globalConfigPath);

        Assert.Equal(0, result);
        Assert.False(File.Exists(globalConfigPath));

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.Equal("Chinese", workspaceNode["Language"]?.GetValue<string>());
        Assert.Equal("sk-local", workspaceNode["ApiKey"]?.GetValue<string>());
        Assert.Equal("https://local.example/v1", workspaceNode["EndPoint"]?.GetValue<string>());
        Assert.Equal("deepseek-chat", workspaceNode["Model"]?.GetValue<string>());
        Assert.Contains("个人助理", File.ReadAllText(Path.Combine(craftPath, "AGENTS.md")));
    }

    [Fact]
    public void RunSetup_PreferExistingUserConfig_WritesWorkspaceOverridesOnly()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(globalConfigPath)!);
        File.WriteAllText(globalConfigPath, """
        {
          "Language": "English",
          "ApiKey": "sk-global",
          "EndPoint": "https://example.com/v1",
          "Model": "gpt-4o-mini"
        }
        """);

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            Language = Language.English,
            ApiKey = "",
            EndPoint = "https://workspace.example/v1",
            Model = "gpt-4.1",
            Profile = WorkspaceBootstrapProfile.Default,
            PreferExistingUserConfig = true
        }, globalConfigPath);

        Assert.Equal(0, result);

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        Assert.Equal("https://example.com/v1", globalNode["EndPoint"]?.GetValue<string>());
        Assert.Equal("gpt-4o-mini", globalNode["Model"]?.GetValue<string>());
        Assert.Equal("sk-global", globalNode["ApiKey"]?.GetValue<string>());

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("Language", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("ApiKey", workspaceNode.Select(p => p.Key));
        Assert.Equal("https://workspace.example/v1", workspaceNode["EndPoint"]?.GetValue<string>());
        Assert.Equal("gpt-4.1", workspaceNode["Model"]?.GetValue<string>());
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
