using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerWelcomeSuggestionsTests : IDisposable
{
    private readonly FakeWelcomeSuggestionService _welcomeSuggestionService = new();
    private readonly AppServerTestHarness _h;

    public AppServerWelcomeSuggestionsTests()
    {
        _h = new AppServerTestHarness(welcomeSuggestionService: _welcomeSuggestionService);
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Initialize_AdvertisesWelcomeSuggestionsCapability()
    {
        var initDoc = await _h.InitializeAsync();

        var extensions = initDoc.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("extensions");

        Assert.True(extensions.GetProperty("welcomeSuggestions").GetBoolean());
    }

    [Fact]
    public async Task WelcomeSuggestions_RoutesToServiceAndReturnsTypedPayload()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest(AppServerMethods.WelcomeSuggestions, new
        {
            identity = new
            {
                channelName = "dotcraft-desktop",
                userId = "local",
                workspacePath = _h.Identity.WorkspacePath,
                channelContext = $"workspace:{_h.Identity.WorkspacePath}"
            },
            maxItems = 4
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("dynamic", result.GetProperty("source").GetString());
        Assert.Equal(4, result.GetProperty("items").GetArrayLength());
        Assert.NotNull(_welcomeSuggestionService.LastParams);
        Assert.Equal("dotcraft-desktop", _welcomeSuggestionService.LastParams!.Identity.ChannelName);
    }

    [Fact]
    public async Task WelcomeSuggestions_BeforeInitialize_ReturnsNotInitialized()
    {
        var msg = _h.BuildRequest(AppServerMethods.WelcomeSuggestions, new
        {
            identity = new
            {
                channelName = "dotcraft-desktop",
                workspacePath = _h.Identity.WorkspacePath
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.NotInitializedCode);
    }

    private sealed class FakeWelcomeSuggestionService : IWelcomeSuggestionService
    {
        public WelcomeSuggestionsParams? LastParams { get; private set; }

        public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null)
        {
        }

        public Task<WelcomeSuggestionsResult> SuggestAsync(
            WelcomeSuggestionsParams parameters,
            CancellationToken cancellationToken = default)
        {
            LastParams = parameters;
            return Task.FromResult(new WelcomeSuggestionsResult
            {
                Source = "dynamic",
                Fingerprint = "test-fingerprint",
                GeneratedAt = DateTimeOffset.UtcNow,
                Items =
                [
                    new WelcomeSuggestionItem { Title = "One", Prompt = "Prompt one", Reason = "Reason one" },
                    new WelcomeSuggestionItem { Title = "Two", Prompt = "Prompt two", Reason = "Reason two" },
                    new WelcomeSuggestionItem { Title = "Three", Prompt = "Prompt three", Reason = "Reason three" },
                    new WelcomeSuggestionItem { Title = "Four", Prompt = "Prompt four", Reason = "Reason four" }
                ]
            });
        }
    }
}
