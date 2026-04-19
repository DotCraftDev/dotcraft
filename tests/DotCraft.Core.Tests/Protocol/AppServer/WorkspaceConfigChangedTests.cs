using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using System.Text.Json;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class WorkspaceConfigChangedTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"workspace_config_changed_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public WorkspaceConfigChangedTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new { model = "gpt-4o-mini" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceModel);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ApiKeyOnly_EmitsWorkspaceApiKeyRegion()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new { apiKey = "sk-live-key" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceApiKey);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_EndPointOnly_EmitsWorkspaceEndPointRegion()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new { endPoint = "https://example.com/v1" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceEndPoint);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ModelApiKeyEndPoint_EmitsAllWorkspaceRegions()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            model = "gpt-4o-mini",
            apiKey = "sk-live-key",
            endPoint = "https://example.com/v1"
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChangedRegions(
            sent,
            AppServerMethods.WorkspaceConfigUpdate,
            [
                ConfigChangeRegions.WorkspaceModel,
                ConfigChangeRegions.WorkspaceApiKey,
                ConfigChangeRegions.WorkspaceEndPoint
            ]);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_NoEffectiveChange_DoesNotEmitWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var firstReq = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            model = "gpt-4o-mini",
            apiKey = "sk-live-key",
            endPoint = "https://example.com/v1"
        });
        await harness.ExecuteRequestAsync(firstReq);
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var secondReq = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            model = "gpt-4o-mini",
            apiKey = "sk-live-key",
            endPoint = "https://example.com/v1"
        });
        await harness.ExecuteRequestAsync(secondReq);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(sent);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_PreservesUnrelatedFieldsAndKeyCasing()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "model": "gpt-legacy",
              "apikey": "sk-old",
              "endpoint": "https://old.example.com/v1",
              "Theme": "dark"
            }
            """);

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            model = "gpt-4o-mini",
            apiKey = "sk-live-key",
            endPoint = "https://example.com/v1"
        });
        await harness.ExecuteRequestAsync(req);

        var json = await File.ReadAllTextAsync(configPath);
        Assert.Contains("\"model\": \"gpt-4o-mini\"", json, StringComparison.Ordinal);
        Assert.Contains("\"apikey\": \"sk-live-key\"", json, StringComparison.Ordinal);
        Assert.Contains("\"endpoint\": \"https://example.com/v1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Theme\": \"dark\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Model\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ApiKey\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"EndPoint\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkillsSetEnabled_EmitsWorkspaceConfigChanged()
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();
        var skillName = loader.ListSkills(filterUnavailable: false).First().Name;

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.SkillsSetEnabled, new { name = skillName, enabled = false });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.SkillsSetEnabled, ConfigChangeRegions.Skills);
    }

    [Fact]
    public async Task McpUpsert_EmitsWorkspaceConfigChanged()
    {
        var manager = new McpClientManager();
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            mcpClientManager: manager);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.McpUpsert, new
        {
            server = new
            {
                name = "demo",
                enabled = false,
                transport = "streamableHttp",
                url = "https://example.com/mcp"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.McpUpsert, ConfigChangeRegions.Mcp);
    }

    [Fact]
    public async Task McpRemove_EmitsWorkspaceConfigChanged()
    {
        var manager = new McpClientManager();
        await manager.UpsertAsync(new McpServerConfig
        {
            Name = "demo",
            Enabled = false,
            Transport = "streamableHttp",
            Url = "https://example.com/mcp"
        });

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            mcpClientManager: manager);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.McpRemove, new { name = "demo" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.McpRemove, ConfigChangeRegions.Mcp);
    }

    [Fact]
    public async Task ExternalChannelUpsert_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "telegram",
                enabled = true,
                transport = "websocket"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.ExternalChannelUpsert, ConfigChangeRegions.ExternalChannel);
    }

    [Fact]
    public async Task ExternalChannelRemove_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var upsert = harness.BuildRequest(AppServerMethods.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "telegram",
                enabled = true,
                transport = "websocket"
            }
        });
        await harness.ExecuteRequestAsync(upsert);
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var remove = harness.BuildRequest(AppServerMethods.ExternalChannelRemove, new { name = "telegram" });
        await harness.ExecuteRequestAsync(remove);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.ExternalChannelRemove, ConfigChangeRegions.ExternalChannel);
    }

    [Fact]
    public async Task FailedWrite_DoesNotEmitWorkspaceConfigChanged()
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.SkillsSetEnabled, new { name = "missing_skill", enabled = true });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(sent);
        AppServerTestHarness.AssertIsErrorResponse(sent[0], AppServerErrors.SkillNotFoundCode);
    }

    [Fact]
    public async Task ReadMethods_DoNotEmitWorkspaceConfigChanged()
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();
        var manager = new McpClientManager();

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader,
            mcpClientManager: manager);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var skillsList = harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true });
        await harness.ExecuteRequestAsync(skillsList);
        var skillsSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(skillsSent);

        var mcpList = harness.BuildRequest(AppServerMethods.McpList, new { });
        await harness.ExecuteRequestAsync(mcpList);
        var mcpSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(mcpSent);
    }

    [Fact]
    public async Task ConfigChangeCapabilityFalse_SuppressesWireNotification_ButMonitorStillFires()
    {
        var monitorEvents = new List<AppConfigChangedEventArgs>();
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        harness.Monitor.Changed += OnChanged;
        await harness.InitializeAsync(configChange: false);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new { model = "gpt-4o-mini" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(sent);
        Assert.Single(monitorEvents);
        Assert.Equal(AppServerMethods.WorkspaceConfigUpdate, monitorEvents[0].Source);
        Assert.Contains(ConfigChangeRegions.WorkspaceModel, monitorEvents[0].Regions);

        harness.Monitor.Changed -= OnChanged;

        void OnChanged(object? sender, AppConfigChangedEventArgs e)
        {
            monitorEvents.Add(e);
        }
    }

    private static IDisposable AttachConfigChangedBridge(AppServerTestHarness harness)
    {
        void OnChanged(object? sender, AppConfigChangedEventArgs change)
        {
            if (!harness.Connection.SupportsConfigChange || !harness.Connection.ShouldSendNotification(AppServerMethods.WorkspaceConfigChanged))
                return;

            var notification = new
            {
                jsonrpc = "2.0",
                method = AppServerMethods.WorkspaceConfigChanged,
                @params = new WorkspaceConfigChangedParams
                {
                    Source = change.Source,
                    Regions = [.. change.Regions],
                    ChangedAt = change.ChangedAt
                }
            };
            harness.Transport.WriteMessageAsync(notification).GetAwaiter().GetResult();
        }

        harness.Monitor.Changed += OnChanged;
        return new ActionOnDispose(() => harness.Monitor.Changed -= OnChanged);
    }

    private static void AssertSingleConfigChanged(
        IReadOnlyList<JsonDocument> sent,
        string expectedSource,
        string expectedRegion)
    {
        var notifications = sent
            .Where(d =>
                d.RootElement.TryGetProperty("method", out var method)
                && string.Equals(method.GetString(), AppServerMethods.WorkspaceConfigChanged, StringComparison.Ordinal))
            .ToList();
        Assert.Single(notifications);

        var payload = notifications[0].RootElement.GetProperty("params");
        Assert.Equal(expectedSource, payload.GetProperty("source").GetString());
        Assert.Contains(expectedRegion, payload.GetProperty("regions").EnumerateArray().Select(v => v.GetString()));
        _ = payload.GetProperty("changedAt").GetDateTimeOffset();
    }

    private static void AssertSingleConfigChangedRegions(
        IReadOnlyList<JsonDocument> sent,
        string expectedSource,
        IReadOnlyList<string> expectedRegions)
    {
        var notifications = sent
            .Where(d =>
                d.RootElement.TryGetProperty("method", out var method)
                && string.Equals(method.GetString(), AppServerMethods.WorkspaceConfigChanged, StringComparison.Ordinal))
            .ToList();
        Assert.Single(notifications);

        var payload = notifications[0].RootElement.GetProperty("params");
        Assert.Equal(expectedSource, payload.GetProperty("source").GetString());
        var regions = payload.GetProperty("regions").EnumerateArray()
            .Select(v => v.GetString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .ToList();
        Assert.Equal(expectedRegions.Count, regions.Count);
        foreach (var region in expectedRegions)
            Assert.Contains(region, regions);
        _ = payload.GetProperty("changedAt").GetDateTimeOffset();
    }

    private static void AssertNoConfigChanged(IReadOnlyList<JsonDocument> sent)
    {
        Assert.DoesNotContain(
            sent,
            d => d.RootElement.TryGetProperty("method", out var method)
                 && string.Equals(method.GetString(), AppServerMethods.WorkspaceConfigChanged, StringComparison.Ordinal));
    }

    private sealed class ActionOnDispose(Action disposeAction) : IDisposable
    {
        public void Dispose() => disposeAction();
    }
}
