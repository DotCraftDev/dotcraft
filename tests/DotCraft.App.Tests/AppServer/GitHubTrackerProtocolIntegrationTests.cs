using DotCraft.GitHubTracker.Protocol.AppServer;
using DotCraft.Protocol.AppServer;
using DotCraft.Tests.Sessions.Protocol.AppServer;

namespace DotCraft.Tests.AppServer;

public sealed class GitHubTrackerProtocolIntegrationTests : IDisposable
{
    private readonly string _craftDir;
    private readonly AppServerTestHarness _h;

    public GitHubTrackerProtocolIntegrationTests()
    {
        _craftDir = Path.Combine(
            Path.GetTempPath(),
            "GitHubTrackerProtocolTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_craftDir);

        _h = new AppServerTestHarness(
            protocolExtensions: [new GitHubTrackerAppServerExtension(new GitHubTrackerConfigProtocolService())],
            workspaceCraftPath: _craftDir);
    }

    public void Dispose()
    {
        _h.Dispose();
        try { Directory.Delete(_craftDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Initialize_AdvertisesGitHubTrackerCompatibilityAndExtensionCapabilities()
    {
        var initDoc = await _h.InitializeAsync();
        var caps = initDoc.RootElement.GetProperty("result").GetProperty("capabilities");

        Assert.True(caps.GetProperty("gitHubTrackerConfig").GetBoolean());
        Assert.True(caps.GetProperty("extensions").GetProperty("githubTrackerConfig").GetBoolean());
    }

    [Fact]
    public async Task GitHubTrackerGet_ReturnsDefaultShape()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest(GitHubTrackerAppServerMethods.Get);
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);

        var config = doc.RootElement.GetProperty("result").GetProperty("config");
        Assert.Equal("WORKFLOW.md", config.GetProperty("issuesWorkflowPath").GetString());
        Assert.Equal("PR_WORKFLOW.md", config.GetProperty("pullRequestWorkflowPath").GetString());
    }

    [Fact]
    public async Task GitHubTrackerUpdate_PreservesMaskedApiKey()
    {
        await _h.InitializeAsync();

        await _h.ExecuteRequestAsync(_h.BuildRequest(GitHubTrackerAppServerMethods.Update, new
        {
            config = new
            {
                enabled = true,
                issuesWorkflowPath = "WORKFLOW.md",
                pullRequestWorkflowPath = "PR_WORKFLOW.md",
                tracker = new
                {
                    apiKey = "secret-token",
                    repository = "owner/repo"
                }
            }
        }));
        _ = await _h.Transport.ReadNextSentAsync();

        await _h.ExecuteRequestAsync(_h.BuildRequest(GitHubTrackerAppServerMethods.Update, new
        {
            config = new
            {
                enabled = true,
                issuesWorkflowPath = "WORKFLOW.md",
                pullRequestWorkflowPath = "PR_WORKFLOW.md",
                tracker = new
                {
                    apiKey = "***",
                    repository = "owner/repo"
                }
            }
        }));

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.Equal(
            "***",
            doc.RootElement.GetProperty("result").GetProperty("config").GetProperty("tracker").GetProperty("apiKey").GetString());

        await _h.ExecuteRequestAsync(_h.BuildRequest(GitHubTrackerAppServerMethods.Get));
        var getDoc = await _h.Transport.ReadNextSentAsync();
        Assert.Equal(
            "***",
            getDoc.RootElement.GetProperty("result").GetProperty("config").GetProperty("tracker").GetProperty("apiKey").GetString());
    }

    [Fact]
    public async Task GitHubTrackerUpdate_InvalidPayload_ReturnsValidationError()
    {
        await _h.InitializeAsync();

        await _h.ExecuteRequestAsync(_h.BuildRequest(GitHubTrackerAppServerMethods.Update, new
        {
            config = new
            {
                enabled = true,
                tracker = new
                {
                    repository = "",
                    activeStates = new[] { "Todo", "Done" },
                    terminalStates = new[] { "Done" }
                }
            }
        }));

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.GitHubTrackerConfigValidationFailedCode);
    }
}
